param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "AuraSharedCore.Tests\AuraSharedCore.Tests.csproj"

dotnet run --project $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "AuraSharedCore behavior tests failed."
}

Write-Host "AuraSharedCore behavior validation passed."
