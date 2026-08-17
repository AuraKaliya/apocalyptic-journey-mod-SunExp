param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "AuraToolingShared.Tests\AuraToolingShared.Tests.csproj"

dotnet run --project $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "AuraTooling.Shared tests failed with exit code $LASTEXITCODE."
}

Write-Host "AuraTooling.Shared protocol tests passed."
