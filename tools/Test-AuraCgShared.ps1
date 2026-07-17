param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "AuraCgShared.Tests\AuraCgShared.Tests.csproj"

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "AuraCgShared test project is missing: $project"
}

dotnet run --project $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "AuraCgShared tests failed."
}
