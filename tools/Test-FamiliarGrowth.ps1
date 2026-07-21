param()

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$registryPath = Join-Path $repoRoot "Terrias\familiar.blessing.registry.json"
$projectPath = Join-Path $repoRoot "Terrias-Dev.FamiliarTests\Terrias-Dev.FamiliarTests.csproj"

if (-not (Test-Path -LiteralPath $registryPath)) {
    throw "Familiar blessing registry is missing."
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Familiar C# test project is missing."
}

& dotnet run --project $projectPath -c Release -- $registryPath
if ($LASTEXITCODE -ne 0) {
    throw "Familiar growth C# tests failed."
}
