param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "Terrias-Dev.SpiritTests\Terrias-Dev.SpiritTests.csproj"
$artifactRegistry = Join-Path $repoRoot "Terrias\spirit.artifact.registry.json"

dotnet run --project $project -c $Configuration -- $artifactRegistry
if ($LASTEXITCODE -ne 0) {
    throw "Terrias spirit runtime behavior tests failed."
}

Write-Host "Terrias spirit runtime behavior validation passed."
