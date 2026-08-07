param(
    [string]$Configuration = "Release",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "AuraCombatAiShared.Tests\AuraCombatAiShared.Tests.csproj"
$arguments = @("run", "--project", $project, "-c", $Configuration)
if ($NoRestore) {
    $arguments += "--no-restore"
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Aura combat AI behavior tests failed with exit code $LASTEXITCODE."
}

Write-Host "Aura combat AI behavior tests passed."
