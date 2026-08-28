param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "Terrias-Dev.RegistryTests\Terrias-Dev.RegistryTests.csproj"
$intentPath = Join-Path $repoRoot "Terrias\spirit.intent.registry.json"
$artifactPath = Join-Path $repoRoot "Terrias\spirit.artifact.registry.json"

dotnet run --project $project -c $Configuration -- $intentPath $artifactPath
if ($LASTEXITCODE -ne 0) {
    throw "Terrias spirit registry behavior tests failed."
}

Write-Host "Terrias spirit registry behavior validation passed."
