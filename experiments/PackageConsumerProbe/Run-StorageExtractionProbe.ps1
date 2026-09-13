#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $OldStoragePackageSource,
    [Parameter(Mandatory)][string] $OldStorageVersion,
    [string] $DurableGraphPackageSource,
    [string] $DurableGraphVersion,
    [string] $StoragePackageSource,
    [string] $StorageVersion,
    [ValidateSet('All', 'Seed', 'Complete')][string] $Stage = 'All',
    [string] $WorkRoot
)

# Both lanes use the very same frozen DG packages and source files. Only the five
# Storage packages change. This runner consumes packages; it never packs sources.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$utf8 = [Text.UTF8Encoding]::new($false)
$storageNames = @('Atelia.Primitives', 'Atelia.Data', 'Atelia.Rbf', 'Atelia.RbfSegmentStore', 'Atelia.EventJournal')
$graphNames = @('Atelia.DurableGraph.Serialization', 'Atelia.DurableGraph', 'Atelia.DurableGraph.Storage', 'Atelia.DurableGraph.Persistence')
if (!$DurableGraphPackageSource) { $DurableGraphPackageSource = $OldStoragePackageSource }
if (!$DurableGraphVersion) { $DurableGraphVersion = $OldStorageVersion }
if ($Stage -ne 'Seed') {
    if (!$StoragePackageSource -or !$StorageVersion) { throw 'All/Complete requires -StoragePackageSource and -StorageVersion.' }
    if ($StorageVersion -eq $OldStorageVersion) { throw 'The old and new Storage versions must be distinct.' }
    $StoragePackageSource = (Resolve-Path -LiteralPath $StoragePackageSource).Path
}
if (!$WorkRoot) {
    if ($Stage -eq 'Complete') { throw 'Complete requires the WorkRoot produced by Seed.' }
    $WorkRoot = Join-Path $PSScriptRoot "obj/storage-extraction-$([Guid]::NewGuid().ToString('N'))"
}
$WorkRoot = [IO.Path]::GetFullPath($WorkRoot)
$oldRoot = Join-Path $WorkRoot 'old'
$seedDatabase = Join-Path $WorkRoot 'seed-database'
$evidence = Join-Path $WorkRoot 'seed-evidence'
$seedManifest = Join-Path $WorkRoot 'seed.json'

function Invoke-DotNet([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed ($LASTEXITCODE)." }
}
function Write-Json([string] $Path, $Value) {
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 8), $utf8)
}
function Get-Tree([string] $Root, [switch] $IncludeTimestamp) {
    $result = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
        $stamp = if ($IncludeTimestamp) { "|$($file.LastWriteTimeUtc.Ticks)" } else { '' }
        $result[$relative] = "$($file.Length)|$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)$stamp"
    }
    return $result
}
function Assert-Tree([string] $Root, $Expected, [switch] $IncludeTimestamp) {
    $actual = Get-Tree $Root -IncludeTimestamp:$IncludeTimestamp
    if ($actual.Count -ne $Expected.Count) { throw "File set changed: $Root" }
    foreach ($key in $Expected.Keys) {
        if (!$actual.ContainsKey($key) -or $actual[$key] -cne $Expected[$key]) { throw "File changed: $Root/$key" }
    }
}
function Copy-Package([string] $Source, [string] $Name, [string] $Version, [string] $Feed) {
    $path = Join-Path $Source "$Name.$Version.nupkg"
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing input package: $path" }
    Copy-Item -LiteralPath $path -Destination $Feed
}
function Build-Lane([string] $Root, [string] $Storage, [string] $StorageFeed, [bool] $IsOld) {
    if (Test-Path -LiteralPath $Root) { throw "Refusing to overwrite a lane: $Root" }
    $feed = Join-Path $Root 'feed'
    $history = Join-Path $Root 'history'
    $null = New-Item -ItemType Directory -Path $Root, $feed, $history
    foreach ($name in $storageNames) { Copy-Package $StorageFeed $name $Storage $feed }
    $graphFeed = if ($IsOld) { $DurableGraphPackageSource } else { Join-Path $oldRoot 'feed' }
    foreach ($name in $graphNames) { Copy-Package $graphFeed $name $DurableGraphVersion $feed }
    $source = if ($IsOld) { Join-Path $PSScriptRoot 'StorageExtractionConsumer' } else { $oldRoot }
    foreach ($name in @('Consumer.csproj', 'Program.cs', 'Model.cs')) {
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination $Root
    }
    # Stop parent repository props/targets and ambient NuGet sources at this boundary.
    [IO.File]::WriteAllText((Join-Path $Root 'Directory.Build.props'), '<Project />', $utf8)
    [IO.File]::WriteAllText((Join-Path $Root 'Directory.Build.targets'), '<Project />', $utf8)
    $config = Join-Path $Root 'NuGet.Config'
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    [IO.File]::WriteAllText($config, "<configuration><packageSources><clear /><add key=`"witness`" value=`"$escapedFeed`" /></packageSources><packageSourceMapping><clear /></packageSourceMapping><fallbackPackageFolders><clear /></fallbackPackageFolders></configuration>", $utf8)
    if (!$IsOld) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $oldRoot 'history') -File) {
            Copy-Item -LiteralPath $file.FullName -Destination $history
        }
    }
    $project = Join-Path $Root 'Consumer.csproj'
    $properties = @("-p:DurableGraphPackageVersion=$DurableGraphVersion", "-p:StoragePackageVersion=$Storage",
        "-p:DurableGraphSchemaHistoryDirectory=$history")
    Invoke-DotNet (@('restore', $project, '--configfile', $config, '--packages', (Join-Path $Root 'packages')) + $properties)
    $historyMode = if ($IsOld) { 'Publish' } else { 'Verify' }
    Invoke-DotNet (@('build', $project, '--no-restore', '--configuration', 'Release',
        "-p:DurableGraphSchemaHistoryMode=$historyMode", '-v:q') + $properties)
    if (@(Get-ChildItem -LiteralPath $history -Filter '*.dgschema' -File).Count -ne 2) {
        throw 'Expected exactly two accepted model schema files.'
    }
    if (!$IsOld) { Assert-Tree $history (Get-Tree (Join-Path $oldRoot 'history')) }
    $assets = Get-Content -LiteralPath (Join-Path $Root 'obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($assets.libraries.Count -ne 9) { throw "Expected exactly nine package libraries, found $($assets.libraries.Count)." }
    $provenance = foreach ($name in $storageNames + $graphNames) {
        $version = if ($name -in $storageNames) { $Storage } else { $DurableGraphVersion }
        $key = "$name/$version"
        if (!$assets.libraries.ContainsKey($key) -or $assets.libraries[$key].type -ne 'package') {
            throw "Expected exact restored package: $key"
        }
        $package = Join-Path $feed "$name.$version.nupkg"
        [pscustomobject]@{ Name = $name; Version = $version; SHA256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash }
    }
    Write-Json (Join-Path $Root 'package-provenance.json') @($provenance)
    $generator = "packages/atelia.durablegraph/$DurableGraphVersion/analyzers/dotnet/cs/Atelia.DurableGraph.Generator.dll"
    if (!$IsOld -and (Get-FileHash -LiteralPath (Join-Path $Root $generator)).Hash -cne
        (Get-FileHash -LiteralPath (Join-Path $oldRoot $generator)).Hash) { throw 'Generator changed between the two lanes.' }
}
function Invoke-Consumer([string] $Root, [string] $Mode, [string] $Database, [string] $Storage, [string] $LogRoot) {
    $assembly = Join-Path $Root 'bin/Release/net10.0/Atelia.StorageExtractionConsumer.dll'
    $lines = @(& dotnet $assembly $Mode $Database $evidence)
    if ($LASTEXITCODE -ne 0 -or $lines.Count -eq 0 -or $lines[-1] -cne "StorageExtraction:${Mode}:Passed") {
        throw "Consumer $Mode failed: $($lines -join [Environment]::NewLine)"
    }
    $loaded = @($lines | Where-Object { $_.StartsWith('Loaded:') } | ForEach-Object { $_.Substring(7) | ConvertFrom-Json })
    if ($loaded.Count -ne 9) { throw 'Expected nine actual loaded assembly records.' }
    foreach ($name in $storageNames + $graphNames) {
        $records = @($loaded | Where-Object { $_.Name -ceq $name })
        if ($records.Count -ne 1) { throw "Missing or repeated loaded assembly: $name" }
        $version = if ($name -in $storageNames) { $Storage } else { $DurableGraphVersion }
        $cached = Join-Path $Root "packages/$($name.ToLowerInvariant())/$version/lib/net10.0/$name.dll"
        $outputDll = Join-Path (Split-Path $assembly) "$name.dll"
        if ([IO.Path]::GetFullPath($records[0].Location) -ne [IO.Path]::GetFullPath($outputDll) -or
            $records[0].SHA256 -cne (Get-FileHash -LiteralPath $cached -Algorithm SHA256).Hash) {
            throw "Actual loaded DLL is not the selected restored package: $name"
        }
        if ($Root -ne $oldRoot -and $name -in $graphNames -and $records[0].SHA256 -cne
            (Get-FileHash -LiteralPath (Join-Path $oldRoot "bin/Release/net10.0/$name.dll") -Algorithm SHA256).Hash) {
            throw "DG runtime bytes changed across Storage lanes: $name"
        }
    }
    [IO.File]::WriteAllLines((Join-Path $LogRoot "$Mode.log"), [string[]] $lines, $utf8)
    Write-Host $lines[-1]
}
function Assert-OldFrames([string] $Database) {
    $frames = @(Get-Content -LiteralPath (Join-Path $evidence 'old-frames.json') -Raw | ConvertFrom-Json)
    foreach ($frame in $frames) {
        $path = Join-Path $Database $frame.Path
        $bytes = [IO.File]::ReadAllBytes($path)
        if ($frame.Offset -lt 0 -or $frame.Length -le 0 -or $frame.Offset + $frame.Length -gt $bytes.Length) {
            throw "Old frame disappeared: $($frame.Path):$($frame.Offset)"
        }
        $oldFrame = [byte[]]::new($frame.Length)
        [Array]::Copy($bytes, [long] $frame.Offset, $oldFrame, 0L, [long] $frame.Length)
        if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($oldFrame)) -cne $frame.SHA256) {
            throw "Old complete frame changed: $($frame.Path):$($frame.Offset)"
        }
    }
    return $frames.Count
}

if ($Stage -ne 'Complete') {
    if (Test-Path -LiteralPath $WorkRoot) { throw "Seed requires a fresh WorkRoot: $WorkRoot" }
    $OldStoragePackageSource = (Resolve-Path -LiteralPath $OldStoragePackageSource).Path
    $DurableGraphPackageSource = (Resolve-Path -LiteralPath $DurableGraphPackageSource).Path
    $null = New-Item -ItemType Directory -Path $WorkRoot
    Build-Lane $oldRoot $OldStorageVersion $OldStoragePackageSource $true
    Invoke-Consumer $oldRoot 'seed' $seedDatabase $OldStorageVersion $WorkRoot
    $null = Assert-OldFrames $seedDatabase
    Write-Json $seedManifest @{
        OldStorageVersion = $OldStorageVersion; DurableGraphVersion = $DurableGraphVersion
        OldLane = (Get-Tree $oldRoot); Database = (Get-Tree $seedDatabase); Evidence = (Get-Tree $evidence)
    }
    Write-Host "StorageExtraction:FrozenOldWriterAndData:Passed; WorkRoot: $WorkRoot"
}
if ($Stage -ne 'Seed') {
    $seed = Get-Content -LiteralPath $seedManifest -Raw | ConvertFrom-Json -AsHashtable
    if ($seed.OldStorageVersion -cne $OldStorageVersion -or $seed.DurableGraphVersion -cne $DurableGraphVersion) {
        throw 'Requested versions do not match the frozen seed.'
    }
    Assert-Tree $oldRoot $seed.OldLane
    Assert-Tree $seedDatabase $seed.Database
    Assert-Tree $evidence $seed.Evidence
    # Every attempt starts from a copy; failed builds/continuations never require reseeding.
    $attempt = Join-Path $WorkRoot "attempts/$([Guid]::NewGuid().ToString('N'))"
    $null = New-Item -ItemType Directory -Path $attempt
    $current = Join-Path $attempt 'current'
    $database = Join-Path $attempt 'database'
    Copy-Item -LiteralPath $seedDatabase -Destination $database -Recurse
    Assert-Tree $database $seed.Database
    Build-Lane $current $StorageVersion $StoragePackageSource $false
    $before = Get-Tree $database -IncludeTimestamp
    Invoke-Consumer $current 'read' $database $StorageVersion $attempt
    Assert-Tree $database $before -IncludeTimestamp
    Invoke-Consumer $current 'continue' $database $StorageVersion $attempt
    $before = Get-Tree $database -IncludeTimestamp
    Invoke-Consumer $current 'check' $database $StorageVersion $attempt
    Assert-Tree $database $before -IncludeTimestamp
    $frameCount = Assert-OldFrames $database
    Assert-Tree $oldRoot $seed.OldLane
    Assert-Tree $seedDatabase $seed.Database
    Assert-Tree $evidence $seed.Evidence
    $result = "StorageExtraction:SameDG:FiveStoragePackagesChanged:SchemaHistoryUnchanged:RefParentHeadPending:OldFramesUnchanged:${frameCount}:ColdReadAppendColdRead:Passed"
    [IO.File]::WriteAllText((Join-Path $attempt 'result.txt'), $result, $utf8)
    Write-Host $result
    Write-Host "Artifacts: $attempt"
}
