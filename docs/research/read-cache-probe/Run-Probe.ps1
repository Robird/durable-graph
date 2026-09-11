param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('durablegraph-read-cache-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $scratch)
$sources = Join-Path $scratch 'Storage'
[void](New-Item -ItemType Directory -Path $sources)
Copy-Item -Path (Join-Path $repo 'src/DurableGraph.StateStore.Storage/*.cs') -Destination $sources
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ProbeHarness.cs') -Destination (Join-Path $scratch 'Program.cs')
$storePath = Join-Path $sources 'StateRevisionStore.cs'
$source = [IO.File]::ReadAllText($storePath).Replace("`r`n", "`n")
function Replace-Once([string]$Old, [string]$New) {
    if (($script:source.Split([string[]]@($Old), [StringSplitOptions]::None).Count - 1) -ne 1) {
        throw "Source drift: expected exactly one occurrence of $Old"
    }
    $script:source = $script:source.Replace($Old, $New)
}
Replace-Once 'public sealed class StateRevisionStore {' @'
public sealed class StateRevisionStore {
    internal ProbeMemo Memo { get; } = new();
'@
Replace-Once '    public StateRevision Read(FrameAddress address) {' @'
    public StateRevision Read(FrameAddress address) {
        Memo.ReadCalls++;
        Memo.Requested.Add(address);
        if (Memo.TryFrame(address, out StateRevision? cached)) { return cached!; }
        StateRevision decoded = ReadUncached(address);
        Memo.Decodes++;
        Memo.FrameBytes += address.FrameTicket.Length;
        Memo.AddFrame(address, decoded);
        return decoded;
    }

    private StateRevision ReadUncached(FrameAddress address) {
'@
Replace-Once @'
        FrameAddress revisionAddress) =>
        LiveObjectHeadMapMaterializer.Materialize(revisionAddress, Read);
'@ @'
        FrameAddress revisionAddress) {
        Memo.MapCalls++;
        if (Memo.TryMap(revisionAddress, out var cached)) { return cached!; }
        Memo.MapBuilds++;
        var result = LiveObjectHeadMapMaterializer.Materialize(revisionAddress, Read);
        Memo.AddMap(revisionAddress, result);
        return result;
    }
'@
[IO.File]::WriteAllText($storePath, $source)
# Apply the same ownership-only wrapper change to all ablation variants.
$mapPath = Join-Path $sources 'LiveObjectHeadMapMaterializer.cs'
$source = [IO.File]::ReadAllText($mapPath)
Replace-Once 'return new ReadOnlyDictionary<uint, FrameAddress>(heads);' 'return new ProbeFrozenMap(heads);'
[IO.File]::WriteAllText($mapPath, $source)
$references = @(
    (Join-Path $repo 'src/DurableGraph.StateStore.Serialization/DurableGraph.StateStore.Serialization.csproj'),
    (Join-Path $repo '../atelia/src/Data/Data.csproj'),
    (Join-Path $repo '../atelia/src/Primitives/Primitives.csproj'),
    (Join-Path $repo '../atelia/src/Rbf/Rbf.csproj'),
    (Join-Path $repo '../atelia/src/RbfSegmentStore/RbfSegmentStore.csproj')
) | ForEach-Object { '<ProjectReference Include="' + [Security.SecurityElement]::Escape([IO.Path]::GetFullPath($_)) + '" />' }
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <AssemblyName>Atelia.DurableGraph.StateStore.Storage</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>$($references -join "`n")</ItemGroup>
  <ItemGroup>
    <RuntimeHostConfigurationOption Include="System.Runtime.TieredCompilation" Value="false" />
  </ItemGroup>
</Project>
"@
$projectPath = Join-Path $scratch 'ReadCacheProbe.csproj'
[IO.File]::WriteAllText($projectPath, $project)
if (!$OutputPath) { $OutputPath = Join-Path $scratch 'results.json' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
Write-Output "Isolated sources and data: $scratch"
dotnet build $projectPath -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
dotnet (Join-Path $scratch 'bin/Release/net10.0/Atelia.DurableGraph.StateStore.Storage.dll') $OutputPath
if ($LASTEXITCODE -ne 0) { throw 'Probe execution failed.' }
Write-Output "Results: $OutputPath"
