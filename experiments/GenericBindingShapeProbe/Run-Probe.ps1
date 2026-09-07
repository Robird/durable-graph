$ErrorActionPreference = 'Stop'
dotnet run --project (Join-Path $PSScriptRoot 'GenericBindingShapeProbe.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "GenericBindingShapeProbe failed with exit code $LASTEXITCODE."
}
