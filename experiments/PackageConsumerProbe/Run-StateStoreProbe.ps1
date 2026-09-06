[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$consumerProject = Join-Path $PSScriptRoot "StateStoreConsumer/StateStoreConsumer.csproj"
$runId = "state-store-run-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))-$PID-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
$workRoot = Join-Path $PSScriptRoot "obj/$runId"
$feed = Join-Path $workRoot "feed"
$packageCache = Join-Path $workRoot "packages"
$history = Join-Path $workRoot "history"
$intermediate = (Join-Path $workRoot "consumer-obj") + [IO.Path]::DirectorySeparatorChar
$output = (Join-Path $workRoot "consumer-bin") + [IO.Path]::DirectorySeparatorChar
$packageVersion = "0.0.0-state-e2e.$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss')).$PID"
$consumerText = Get-Content -LiteralPath $consumerProject -Raw
foreach ($forbidden in @("<Import", "<ProjectReference", "<Analyzer", "<AdditionalFiles")) {
    if ($consumerText.Contains($forbidden, [StringComparison]::Ordinal)) {
        throw "Consumer contains forbidden manual wiring '$forbidden'."
    }
}

New-Item -ItemType Directory -Path $feed, $packageCache | Out-Null
Push-Location $repositoryRoot
try {
    # The local feed contains the complete product dependency closure. Upstream files are unchanged.
    foreach ($project in @(
        "../atelia/src/Data/Data.csproj",
        "../atelia/src/Primitives/Primitives.csproj",
        "../atelia/src/Rbf/Rbf.csproj",
        "../atelia/src/RbfSegmentStore/RbfSegmentStore.csproj",
        "src/DurableGraph.StateStore.Serialization/DurableGraph.StateStore.Serialization.csproj",
        "src/DurableGraph/DurableGraph.csproj",
        "src/DurableGraph.StateStore.Storage/DurableGraph.StateStore.Storage.csproj",
        "src/DurableGraph.StateStore/DurableGraph.StateStore.csproj"
    )) {
        Invoke-DotNet @("pack", $project, "--configuration", "Release", "--output", $feed, "-p:PackageVersion=$packageVersion")
    }
    $packages = @(Get-ChildItem -LiteralPath $feed -Filter *.nupkg -File)
    if ($packages.Count -ne 8) { throw "Expected 8 dependency packages, found $($packages.Count)." }

    $consumerProperties = @(
        "-p:DurableGraphPackageVersion=$packageVersion",
        "-p:DurableGraphSnapshotHistoryDirectory=$history",
        "-p:BaseIntermediateOutputPath=$intermediate",
        "-p:BaseOutputPath=$output"
    )
    Invoke-DotNet (@("restore", $consumerProject, "--source", $feed, "--packages", $packageCache) + $consumerProperties)
    Invoke-DotNet (@("build", $consumerProject, "--no-restore") + $consumerProperties)
    $historyFiles = @(Get-ChildItem -LiteralPath $history -Filter *.dgsnapshot -File)
    if ($historyFiles.Count -ne 2) { throw "Expected two generated Schema history files, found $($historyFiles.Count)." }

    $consumerAssembly = Join-Path $output "Debug/net10.0/Atelia.StateStoreConsumer.dll"
    $consumerOutput = (& dotnet $consumerAssembly (Join-Path $workRoot "database") | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $consumerOutput -ne "PersistedSchema:True:BaseTypeReference:True:RawDelta:True:ColdTypedRead:True:SharedString:True:ConflictBeforeAppend:True") {
        throw "Packaged StateStore exercise failed; output was '$consumerOutput'."
    }
    Write-Host $consumerOutput
    Write-Host "StateStore package consumer probe passed. Artifacts: $workRoot"
}
finally {
    Pop-Location
}
