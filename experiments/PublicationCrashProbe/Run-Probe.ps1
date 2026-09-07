param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'PublicationCrashProbe.csproj'
if (-not $NoBuild) {
    dotnet build $project --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Publication crash probe build failed.' }
}
$assembly = Join-Path $PSScriptRoot 'bin/Debug/net10.0/Atelia.PublicationCrashProbe.dll'
if (-not (Test-Path -LiteralPath $assembly)) { throw "Missing built probe: $assembly" }
$runRoot = Join-Path $PSScriptRoot ('obj/run-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($runRoot) | Out-Null
$scenarios = @(
    @{ Checkpoint = 'empty'; Expected = '0'; Name = 'empty' },
    @{ Checkpoint = 'before-publication'; Expected = '1'; Name = 'before-publication' },
    @{ Checkpoint = 'after-append'; Expected = '2'; Name = 'after-append' },
    @{ Checkpoint = 'after-flush'; Expected = '2'; Name = 'after-flush' },
    @{ Checkpoint = 'after-flush'; Expected = 'invalid'; Name = 'torn-publication' }
)
foreach ($scenario in $scenarios) {
    $directory = Join-Path $runRoot $scenario.Name
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = (Get-Command dotnet).Source
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @($assembly, 'child', $directory, $scenario.Checkpoint)) { $start.ArgumentList.Add($argument) }
    $child = [Diagnostics.Process]::Start($start)
    try {
        $ready = $child.StandardOutput.ReadLineAsync()
        if (-not $ready.Wait(30000) -or $ready.Result -ne 'READY') {
            if ($child.HasExited) { throw "Writer failed: $($child.StandardError.ReadToEnd())" }
            throw 'Writer did not reach the requested checkpoint.'
        }
        $child.Kill($true)
        if (-not $child.WaitForExit(30000)) { throw 'Writer did not stop after Kill.' }
    }
    finally {
        if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit() }
        $child.Dispose()
    }
    if ($scenario.Name -eq 'torn-publication') {
        $publicationPath = Join-Path $directory 'publication.rbf'
        $bytes = [IO.File]::ReadAllBytes($publicationPath)
        [byte[]]$torn = $bytes[0..($bytes.Length - 5)]
        [IO.File]::WriteAllBytes($publicationPath, $torn)
    }
    dotnet $assembly verify $directory $scenario.Expected
    if ($LASTEXITCODE -ne 0) { throw "Recovery witness failed: $($scenario.Name)" }
    Write-Output "ProcessKillCheckpoint=$($scenario.Name); Passed=True"
}
Write-Output "Artifacts=$runRoot"
