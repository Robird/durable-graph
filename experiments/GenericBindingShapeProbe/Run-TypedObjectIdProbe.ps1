$runName = "typed-object-id-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))-$PID"
$ErrorActionPreference = 'Stop'
$scratch = Join-Path $PSScriptRoot "obj/$runName"
New-Item -ItemType Directory -Path $scratch | Out-Null
$common = @'
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers.Binary;
public readonly record struct ObjectId<T>(uint Value);
public readonly struct PlainId<T> { public readonly uint Value; public PlainId(uint value) => Value = value; }
public interface IIdOps<T> where T : unmanaged { static abstract uint Read(in T value); }
public readonly struct IdOps<T> : IIdOps<ObjectId<T>> { public static uint Read(in ObjectId<T> value) => value.Value; }
public static class Check {
    public static void Type<T>() where T:unmanaged => Console.WriteLine($"TYPE {typeof(T)} SIZE={Unsafe.SizeOf<T>()} REFS={RuntimeHelpers.IsReferenceOrContainsReferences<T>()}");
    public static uint Read<T,TOps>(ref T value) where T:unmanaged where TOps:IIdOps<T> => TOps.Read(in value);
    public static void Bridge<T>() {
        ObjectId<T>[] values = [new(0x01020304)];
        ref ObjectId<T> slot = ref values[0];
        uint raw = Read<ObjectId<T>,IdOps<T>>(ref slot);
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes,raw);
        slot = new(BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Console.WriteLine($"BRIDGE={Convert.ToHexString(bytes)} ID={slot.Value:X8}");
    }
}
'@
$cases = [ordered]@{
  string = @'
public static class Program { public static void Main() { Check.Type<ObjectId<string>>(); Check.Type<PlainId<string>>(); Check.Bridge<string>(); } }
'@
  self = @'
public readonly record struct NodeDto(ObjectId<NodeDto> Next);
public static class Program { public static void Main() { Check.Type<NodeDto>(); Check.Type<ObjectId<NodeDto>>(); Check.Bridge<NodeDto>(); Console.WriteLine(new NodeDto(new(123)).Next.Value); } }
'@
  mutual = @'
public readonly record struct ADto(ObjectId<BDto> Next);
public readonly record struct BDto(ObjectId<ADto> Next);
public static class Program { public static void Main() { Check.Type<ADto>(); Check.Type<BDto>(); Check.Bridge<ADto>(); } }
'@
  generic_self = @'
public readonly record struct NodeDto<T>(T Data,ObjectId<NodeDto<T>> Next) where T:unmanaged;
public static class Program { public static void Main() { Check.Type<NodeDto<int>>(); Check.Type<NodeDto<NodeDto<int>>>(); Check.Type<NodeDto<ObjectId<string>>>(); Check.Bridge<NodeDto<NodeDto<int>>>(); } }
'@
  generic_expanding = @'
public readonly record struct NodeDto<T>(ObjectId<NodeDto<NodeDto<T>>> Next);
public static class Program { public static void Main() { Check.Type<NodeDto<int>>(); Check.Type<NodeDto<NodeDto<int>>>(); Check.Type<NodeDto<string>>(); Check.Bridge<NodeDto<int>>(); } }
'@
  plain_self = @'
public readonly struct NodeDto { public readonly PlainId<NodeDto> Next; public NodeDto(uint value) => Next = new(value); }
public static class Program { public static void Main() { Check.Type<NodeDto>(); Check.Type<PlainId<NodeDto>>(); Console.WriteLine(new NodeDto(123).Next.Value); } }
'@
  plain_expanding = @'
public readonly struct NodeDto<T> { public readonly PlainId<NodeDto<NodeDto<T>>> Next; public NodeDto(uint value) => Next = new(value); }
public static class Program { public static void Main() { Check.Type<NodeDto<int>>(); Check.Type<NodeDto<NodeDto<int>>>(); Console.WriteLine(new NodeDto<int>(123).Next.Value); } }
'@
  plain_mutual = @'
public readonly struct ADto { public readonly PlainId<BDto> Next; public ADto(uint value) => Next = new(value); }
public readonly struct BDto { public readonly PlainId<ADto> Next; public BDto(uint value) => Next = new(value); }
public static class Program { public static void Main() { Check.Type<ADto>(); Check.Type<BDto>(); Console.WriteLine(new ADto(123).Next.Value); } }
'@
  auto_mutual = @'
[StructLayout(LayoutKind.Auto)] public readonly struct AutoId<T> { public readonly uint Value; public AutoId(uint value) => Value=value; }
[StructLayout(LayoutKind.Auto)] public readonly struct ADto { public readonly AutoId<BDto> Next; public ADto(uint value) => Next = new(value); }
[StructLayout(LayoutKind.Auto)] public readonly struct BDto { public readonly AutoId<ADto> Next; public BDto(uint value) => Next = new(value); }
public static class Program { public static void Main() { Check.Type<ADto>(); Check.Type<BDto>(); Console.WriteLine(new ADto(123).Next.Value); } }
'@
  auto_expanding = @'
[StructLayout(LayoutKind.Auto)] public readonly struct AutoId<T> { public readonly uint Value; public AutoId(uint value) => Value=value; }
[StructLayout(LayoutKind.Auto)] public readonly struct NodeDto<T> { public readonly AutoId<NodeDto<NodeDto<T>>> Next; public NodeDto(uint value) => Next = new(value); }
public static class Program { public static void Main() { Check.Type<NodeDto<int>>(); Check.Type<NodeDto<NodeDto<int>>>(); Console.WriteLine(new NodeDto<int>(123).Next.Value); } }
'@
  class_mutual = @'
public sealed record ADto(ObjectId<BDto> Next);
public sealed record BDto(ObjectId<ADto> Next);
public static class Program { public static void Main() { Check.Type<ObjectId<ADto>>(); Check.Type<ObjectId<BDto>>(); Check.Bridge<ADto>(); Console.WriteLine(new ADto(new(123)).Next.Value); } }
'@
  class_expanding = @'
public sealed record NodeDto<T>(ObjectId<NodeDto<NodeDto<T>>> Next);
public static class Program { public static void Main() { Check.Type<ObjectId<NodeDto<int>>>(); Check.Type<ObjectId<NodeDto<NodeDto<int>>>>(); Check.Bridge<NodeDto<int>>(); Console.WriteLine(new NodeDto<int>(new(123)).Next.Value); } }
'@
  mapped_expanding = @'
public readonly struct NodeDto<TState> where TState:unmanaged {
    public readonly TState Data;
    public readonly ObjectId<NodeDto<ObjectId<NodeDto<TState>>>> Next;
    public NodeDto(TState data, uint next) { Data=data; Next=new(next); }
}
public static class Program { public static void Main() { Check.Type<NodeDto<int>>(); Console.WriteLine(new NodeDto<int>(7,123).Next.Value); } }
'@
  nominal_markers = @'
public sealed class ATarget;
public sealed class BTarget;
public sealed class NodeTarget<T>;
public readonly record struct ADto(ObjectId<BTarget> Next);
public readonly record struct BDto(ObjectId<ATarget> Next);
public readonly record struct NodeDto<T>(ObjectId<NodeTarget<NodeTarget<T>>> Next);
public static class Program { public static void Main() { Check.Type<ADto>(); Check.Type<BDto>(); Check.Type<NodeDto<int>>(); Check.Type<NodeDto<NodeTarget<int>>>(); Check.Bridge<BTarget>(); } }
'@
  typed_properties = @'
public readonly struct ADto {
    private readonly uint _next;
    public ADto(ObjectId<BDto> next) => _next=next.Value;
    public ObjectId<BDto> Next => new(_next);
}
public readonly struct BDto {
    private readonly uint _next;
    public BDto(ObjectId<ADto> next) => _next=next.Value;
    public ObjectId<ADto> Next => new(_next);
}
public readonly struct NodeDto<TState> where TState:unmanaged {
    public readonly TState Data;
    private readonly uint _next;
    public NodeDto(TState data, ObjectId<NodeDto<ObjectId<NodeDto<TState>>>> next) { Data=data; _next=next.Value; }
    public ObjectId<NodeDto<ObjectId<NodeDto<TState>>>> Next => new(_next);
}
public static class Program {
    public static void Main() {
        Check.Type<ADto>(); Check.Type<BDto>(); Check.Type<NodeDto<int>>(); Check.Type<NodeDto<ObjectId<NodeDto<int>>>>();
        if(new ADto(new(42)).Next.Value!=42 || new BDto(new(43)).Next.Value!=43 || new NodeDto<int>(45,new(46)).Next.Value!=46) throw new InvalidOperationException("Typed getter/constructor changed the ID.");
        Check.Bridge<ADto>();
    }
}
'@
  incompatible = @'
public readonly record struct ADto(int Value);
public readonly record struct BDto(int Value);
public static class Program { public static void Main() { ObjectId<ADto> source = new(1); ObjectId<BDto> target = source; Console.WriteLine(target.Value); } }
'@
}
function Run-ProcessBounded([string[]]$Arguments,[string]$WorkDirectory,[int]$TimeoutMs) {
    $info = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $info.WorkingDirectory = $WorkDirectory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $completed = $process.WaitForExit($TimeoutMs)
    if (-not $completed) { $process.Kill($true); $process.WaitForExit() }
    $outcome = [ordered]@{ exitCode=$process.ExitCode; timedOut=(-not $completed); stdout=$stdoutTask.GetAwaiter().GetResult(); stderr=$stderrTask.GetAwaiter().GetResult() }
    $process.Dispose()
    return $outcome
}
$results = [System.Collections.Generic.List[object]]::new()
foreach ($entry in $cases.GetEnumerator()) {
    $directory = Join-Path $scratch $entry.Key
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $directory 'Probe.csproj'),'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><AssemblyName>Probe</AssemblyName><Nullable>enable</Nullable></PropertyGroup></Project>')
    [IO.File]::WriteAllText((Join-Path $directory 'Program.cs'),$common + "`n" + $entry.Value)
    $build = Run-ProcessBounded @('build','Probe.csproj','--verbosity','quiet') $directory 60000
    $run = $null
    if ($build.exitCode -eq 0) { $run = Run-ProcessBounded @('bin/Debug/net10.0/Probe.dll') $directory 10000 }
    $result = [ordered]@{ name=$entry.Key; build=$build; run=$run }
    $results.Add($result)
    $result | ConvertTo-Json -Depth 6 -Compress
}
$results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $scratch 'results.json') -Encoding utf8

# These are observed .NET 10 loader boundaries, not desired product behavior. A
# later runtime accepting a formerly rejected case requires reviewing this witness.
$loaderFailures = @('mutual', 'generic_expanding', 'plain_expanding', 'plain_mutual', 'auto_mutual', 'auto_expanding', 'mapped_expanding')
foreach ($result in $results) {
    if ($result.build.timedOut -or ($null -ne $result.run -and $result.run.timedOut)) {
        throw "Typed ID witness '$($result.name)' timed out. Artifacts: $scratch"
    }
    if ($result.name -eq 'incompatible') {
        if ($result.build.exitCode -eq 0 -or $result.build.stdout -notmatch 'CS0029') {
            throw "Expected incompatible target assignment to fail with CS0029. Artifacts: $scratch"
        }
    } else {
        if ($result.build.exitCode -ne 0) { throw "Unexpected compile failure in '$($result.name)'. Artifacts: $scratch" }
        if ($loaderFailures -contains $result.name) {
            if ($result.run.exitCode -eq 0 -or $result.run.stderr -notmatch 'System.TypeLoadException') {
                throw "Loader behavior changed for '$($result.name)'; review results. Artifacts: $scratch"
            }
        } elseif ($result.run.exitCode -ne 0 -or $result.run.stdout -match 'REFS=True') {
            throw "Expected unmanaged executable case '$($result.name)' failed. Artifacts: $scratch"
        }
    }
}
Write-Host "TypedObjectIdShape:PASS Cases=$($results.Count) Artifacts=$scratch"
