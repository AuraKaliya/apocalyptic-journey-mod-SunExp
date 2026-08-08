param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot `
    "AuraFoundationTrainer.Worker.Tests\AuraFoundationTrainer.Worker.Tests.csproj"

dotnet run --project $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Aura foundation trainer storage tests failed."
}
