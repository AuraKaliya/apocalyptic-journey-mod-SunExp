param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "AuraSkinShared.Tests\AuraSkinShared.Tests.csproj"
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "AuraSkinShared test project is missing: $project"
}

dotnet run --project $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "AuraSkinShared tests failed."
}
