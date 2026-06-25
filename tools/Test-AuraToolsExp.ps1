param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "AuraToolsExp-Dev.Tests\AuraToolsExp-Dev.Tests.csproj"

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "AuraToolsExp test project is missing: $project"
}

dotnet run --project $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "AuraToolsExp tests failed."
}
