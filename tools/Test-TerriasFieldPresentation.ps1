param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
dotnet run --project (Join-Path $repoRoot 'Terrias-Dev.FieldPresentationTests/Terrias-Dev.FieldPresentationTests.csproj') -c $Configuration -- $repoRoot
if ($LASTEXITCODE -ne 0) { throw 'Terrias field presentation behavior checks failed.' }
