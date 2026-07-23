param(
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "AuraCombatAiShared.Tests\AuraCombatAiShared.Tests.csproj"
$arguments = @("run", "--project", $project, "-c", "Release")
if ($NoRestore) {
    $arguments += "--no-restore"
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Aura combat AI tests failed with exit code $LASTEXITCODE."
}
