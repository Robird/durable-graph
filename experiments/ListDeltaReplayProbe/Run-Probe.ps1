[CmdletBinding()]
param(
    [ValidateSet('ordinary', 'whitebox', 'fallback')]
    [string] $Suite = 'ordinary',
    [string] $Counts = '32',
    [int] $Repeats = 1,
    [int] $Rounds = 1,
    [int] $DiffRepeats = 3,
    [int] $Seed = 49001,
    [int] $ReadAmplification = 8,
    [int] $BaseBudgetPercent = 5,
    [string] $Output,
    [switch] $NoBuild
)
$ErrorActionPreference = 'Stop'
if ($Suite -eq 'whitebox' -and -not $PSBoundParameters.ContainsKey('Counts')) { $Counts = '4096' }
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $PSScriptRoot 'ListDeltaReplayProbe.csproj'
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $PSScriptRoot "obj/run-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))-$PID-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
}
Push-Location $repository
try {
    $baseline = (& git rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source baseline.' }
    $dirty = (& git status --porcelain | Out-String).Trim()
    if ($dirty.Length -gt 0) { $baseline += '+working-tree-changes' }
    if (-not $NoBuild) {
        & dotnet build $project -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Replay probe build failed.' }
    }
    & dotnet (Join-Path $PSScriptRoot 'bin/Release/net10.0/Atelia.ListDeltaReplayProbe.dll') `
        --suite $Suite --counts $Counts --repeats $Repeats --rounds $Rounds --diff-repeats $DiffRepeats --seed $Seed `
        --x $ReadAmplification --y $BaseBudgetPercent --output $Output --baseline $baseline
    if ($LASTEXITCODE -ne 0) { throw 'Replay probe failed; preserve artifacts for inspection.' }
}
finally { Pop-Location }
