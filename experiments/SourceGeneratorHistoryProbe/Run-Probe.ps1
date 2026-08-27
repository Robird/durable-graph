Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$probeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = Resolve-Path (Join-Path $probeRoot '..\..')
$projectPath = Join-Path $probeRoot 'Probe\HistoryProbe.csproj'
$generatedRoot = Join-Path $probeRoot 'Probe\obj\history-probe-generated'
$runId = '{0:yyyyMMdd-HHmmss}-{1}' -f (Get-Date), $PID
$runRoot = Join-Path $repositoryRoot "artifacts\SourceGeneratorHistoryProbe\$runId"
$historyRoot = Join-Path $runRoot 'history'

New-Item -ItemType Directory -Path $historyRoot -Force | Out-Null

function Invoke-DotNetSuccess {
    param([string[]]$Arguments)

    & dotnet @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code ${LASTEXITCODE}: dotnet $($Arguments -join ' ')"
    }
}

function Invoke-DotNetFailure {
    param(
        [string[]]$Arguments,
        [string[]]$ExpectedText
    )

    $commandOutput = @(& dotnet @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    Write-Host ($commandOutput -join [Environment]::NewLine)

    if ($exitCode -eq 0) {
        throw "dotnet command unexpectedly succeeded: dotnet $($Arguments -join ' ')"
    }

    $combinedOutput = $commandOutput -join [Environment]::NewLine

    foreach ($expected in $ExpectedText) {
        if (-not $combinedOutput.Contains($expected, [StringComparison]::Ordinal)) {
            throw "Expected failed command output to contain '$expected'."
        }
    }
}

function Build-Arguments {
    param(
        [int]$Version,
        [bool]$Publish,
        [bool]$ResetStaging = $true,
        [string]$Verb = 'build'
    )

    return @(
        $Verb,
        $projectPath,
        '--nologo',
        '-v:minimal',
        "-p:ProbeVersion=$Version",
        "-p:HistoryProbePublish=$($Publish.ToString().ToLowerInvariant())",
        "-p:HistoryProbeResetStaging=$($ResetStaging.ToString().ToLowerInvariant())",
        "-p:HistoryProbeHistoryDirectory=$historyRoot",
        '-p:UseSharedCompilation=false'
    )
}

function Get-HistoryFiles {
    return @(Get-ChildItem -LiteralPath $historyRoot -Filter '*.dgsnapshot' -File | Sort-Object Name)
}

function Assert-HistoryCount {
    param([int]$Expected)

    $actual = @(Get-HistoryFiles).Count

    if ($actual -ne $Expected) {
        throw "Expected $Expected history files, found $actual in '$historyRoot'."
    }
}

Write-Host 'P0: clean the consumer project and start with empty explicit history'
Invoke-DotNetSuccess (Build-Arguments -Version 1 -Publish $false -Verb 'clean')
Assert-HistoryCount 0

Write-Host 'P1: AddSource output alone does not publish an AdditionalFile'
Invoke-DotNetSuccess (Build-Arguments -Version 1 -Publish $false)
Assert-HistoryCount 0
$emittedCandidates = @(
    Get-ChildItem -LiteralPath $generatedRoot -Recurse -Filter '*.dgsnapshot.cs' -File
)

if ($emittedCandidates.Count -ne 1) {
    throw "Expected one physical AddSource candidate, found $($emittedCandidates.Count)."
}

Write-Host 'P2: V2 fails even while the prior V1 AddSource file remains on disk'
Invoke-DotNetFailure `
    -Arguments (Build-Arguments -Version 2 -Publish $true -ResetStaging $false) `
    -ExpectedText @('CS0246', 'CharacterSnapshotV1')
Assert-HistoryCount 0
$candidatesAfterFailure = @(
    Get-ChildItem -LiteralPath $generatedRoot -Recurse -Filter '*.dgsnapshot.cs' -File
)

if ($candidatesAfterFailure.Count -ne 2 ||
    -not ($candidatesAfterFailure.Name -match '\.V1\.') ||
    -not ($candidatesAfterFailure.Name -match '\.V2\.')) {
    throw 'Expected the physical V1 and V2 candidates to remain after the failed build.'
}

Write-Host 'P3: the post-compile hook publishes V1 after a successful V1 build'
Invoke-DotNetSuccess (Build-Arguments -Version 1 -Publish $false -Verb 'clean')
Invoke-DotNetSuccess (Build-Arguments -Version 1 -Publish $true)
Assert-HistoryCount 1
$version1File = @(Get-HistoryFiles)[0]

if ($version1File.Name -notmatch '\.V1\.') {
    throw "Expected only V1 history after the successful V1 publish, found '$($version1File.Name)'."
}

$version1Hash = (Get-FileHash -LiteralPath $version1File.FullName -Algorithm SHA256).Hash

Write-Host 'P4: clean V2 consumes V1 through AdditionalFiles and publishes V2'
Invoke-DotNetSuccess (Build-Arguments -Version 2 -Publish $false -Verb 'clean')
Invoke-DotNetSuccess (Build-Arguments -Version 2 -Publish $true)
Assert-HistoryCount 2
$publishedNames = @(Get-HistoryFiles).Name

if (-not ($publishedNames -match '\.V1\.') -or
    -not ($publishedNames -match '\.V2\.')) {
    throw 'Expected exactly one published V1 snapshot and one published V2 snapshot.'
}

Invoke-DotNetSuccess @(
    'run',
    '--project',
    $projectPath,
    '--no-build',
    "-p:ProbeVersion=2",
    "-p:HistoryProbeHistoryDirectory=$historyRoot"
)

if ((Get-FileHash -LiteralPath $version1File.FullName -Algorithm SHA256).Hash -ne $version1Hash) {
    throw 'Publishing V2 changed the already published V1 snapshot.'
}

$stableHashes = @{}

foreach ($file in Get-HistoryFiles) {
    $stableHashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
}

Write-Host 'P5: clean V2 rebuild is reproducible from accumulated history'
Invoke-DotNetSuccess (Build-Arguments -Version 2 -Publish $false -Verb 'clean')
Invoke-DotNetSuccess (Build-Arguments -Version 2 -Publish $false)
Assert-HistoryCount 2

Write-Host 'P6: repeated publishing is idempotent'
Invoke-DotNetSuccess (Build-Arguments -Version 2 -Publish $false -Verb 'clean')
Invoke-DotNetSuccess (Build-Arguments -Version 2 -Publish $true)
Invoke-DotNetSuccess (Build-Arguments -Version 2 -Publish $false -Verb 'clean')
Invoke-DotNetSuccess (Build-Arguments -Version 2 -Publish $true)
Assert-HistoryCount 2

foreach ($file in Get-HistoryFiles) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash

    if ($stableHashes[$file.Name] -ne $hash) {
        throw "Repeated publishing changed '$($file.Name)'."
    }
}

Write-Host "PASS: explicit one-build-delayed history feedback verified in '$runRoot'."
