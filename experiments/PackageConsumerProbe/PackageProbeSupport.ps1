# Shared preparation only. Each runner continues to own its domain assertions.

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)
    if ($Arguments[0] -eq 'restore') {
        # Runners supply their complete feed(s) with --source. Do not inherit the
        # root config's exact storage-feed mapping for these isolated consumers.
        $config = Join-Path $workRoot 'probe.nuget.config'
        if (!(Test-Path -LiteralPath $config)) {
            [void](New-Item -ItemType Directory -Force -Path $workRoot)
            '<configuration><packageSources><clear /></packageSources><packageSourceMapping><clear /></packageSourceMapping></configuration>' |
                Set-Content -LiteralPath $config -Encoding utf8NoBOM
        }
        $Arguments += @('--configfile', $config)
    }
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function Prepare-DurableGraphProbeFeed {
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)][string] $Version
    )
    if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.nupkg' -File).Count -ne 0) {
        throw 'Prepare the probe in a fresh feed; existing package versions are never overwritten.'
    }
    $storage = & (Join-Path $RepositoryRoot 'eng/Prepare-Storage.ps1')
    if ($storage.Version -ceq $Version) { throw 'Use distinct storage (S) and DurableGraph (G) versions for package probes.' }
    [void](New-Item -ItemType Directory -Force -Path $OutputDirectory)
    foreach ($package in $storage.Packages) {
        Copy-Item -LiteralPath (Join-Path $storage.PackageSource $package.File) -Destination $OutputDirectory
    }
    foreach ($project in @(
        'src/DurableGraph.Serialization/DurableGraph.Serialization.csproj',
        'src/DurableGraph/DurableGraph.csproj',
        'src/DurableGraph.Storage/DurableGraph.Storage.csproj',
        'src/DurableGraph.Persistence/DurableGraph.Persistence.csproj'
    )) {
        # The five storage projects are never repacked with this downstream G.
        & dotnet pack (Join-Path $RepositoryRoot $project) --configuration Release --output $OutputDirectory "-p:PackageVersion=$Version" "-p:StoragePackageVersion=$($storage.Version)" -p:UseStorageSources=false
        if ($LASTEXITCODE -ne 0) { throw "Packing $project failed with exit code $LASTEXITCODE." }
    }
    if (@(Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.nupkg' -File).Count -ne 9) { throw 'Expected five storage packages at S and four DurableGraph packages at G.' }
    Write-Host "Probe feed: storage S=$($storage.Version), DurableGraph G=$Version; $OutputDirectory"
}
