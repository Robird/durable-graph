Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$probeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $probeRoot 'SnapshotUpgradeShapeProbe.csproj'

function Invoke-BuildSuccess {
    param([string]$ProbeCase)

    & dotnet build $projectPath `
        --nologo `
        -v:minimal `
        "-p:ProbeCase=$ProbeCase" `
        -p:UseSharedCompilation=false

    if ($LASTEXITCODE -ne 0) {
        throw "Expected '$ProbeCase' to compile successfully."
    }
}

function Invoke-BuildFailure {
    param(
        [string]$ProbeCase,
        [string]$DiagnosticId
    )

    $commandOutput = @(
        & dotnet build $projectPath `
            --nologo `
            -v:minimal `
            "-p:ProbeCase=$ProbeCase" `
            -p:UseSharedCompilation=false 2>&1
    )
    $exitCode = $LASTEXITCODE
    Write-Host ($commandOutput -join [Environment]::NewLine)

    if ($exitCode -eq 0) {
        throw "Expected '$ProbeCase' to fail compilation."
    }

    if (-not ($commandOutput -join [Environment]::NewLine).Contains(
            $DiagnosticId,
            [StringComparison]::Ordinal)) {
        throw "Expected '$ProbeCase' to report '$DiagnosticId'."
    }
}

Write-Host 'P1: ordinary structs support direct strongly typed in/out upgrades'
Invoke-BuildSuccess 'Valid'
& dotnet run `
    --project $projectPath `
    --no-build `
    "-p:ProbeCase=Valid"

if ($LASTEXITCODE -ne 0) {
    throw 'The valid struct upgrade executable failed.'
}

Write-Host 'P2: omitting one target field fails definite assignment'
Invoke-BuildFailure 'MissingField' 'CS0177'

Write-Host 'P3: assigning default bypasses field-by-field checking'
Invoke-BuildSuccess 'DefaultBypass'
& dotnet run `
    --project $projectPath `
    --no-build `
    "-p:ProbeCase=DefaultBypass"

if ($LASTEXITCODE -ne 0) {
    throw 'The default-bypass executable failed.'
}

Write-Host 'P4: bool false paths must still assign the out value'
Invoke-BuildFailure 'FalseWithoutAssignment' 'CS0177'

Write-Host 'P5: required partial upgrade declarations require implementations'
Invoke-BuildFailure 'MissingPartialImplementation' 'CS8795'

Write-Host 'P6: methods cannot overload by return type alone'
Invoke-BuildFailure 'ReturnTypeOnlyOverload' 'CS0111'

Write-Host 'P7: out-target overloads require an explicit target type at ambiguous calls'
Invoke-BuildFailure 'OutVarAmbiguous' 'CS0121'

Write-Host 'P8: a Snapshot containing string cannot be stackalloc element storage'
Invoke-BuildFailure 'ManagedStackalloc' 'CS0208'

Write-Host 'PASS: Snapshot struct and upgrade signature semantics verified.'
