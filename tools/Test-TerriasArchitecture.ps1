param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Import-Module (Join-Path $repoRoot "tools\modules\ArchitectureBoundaryValidation.psm1") -Force
Invoke-ArchitectureBoundaryValidation -RepoRoot $repoRoot -RuleSet "terrias"

& (Join-Path $repoRoot "tools\Test-TerriasArchitectureGate.ps1")
if ($LASTEXITCODE -ne 0) {
    throw "Terrias architecture semantic fixture validation failed."
}
$semanticProject = Join-Path $repoRoot "tools\TerriasArchitectureGate\TerriasArchitectureGate.csproj"
dotnet run --project $semanticProject -c Release --no-build -- `
    --repo-root $repoRoot `
    --rules (Join-Path $repoRoot "tools\architecture-boundary-rules.json") `
    --exceptions (Join-Path $repoRoot "tools\architecture-boundary-exceptions.json")
if ($LASTEXITCODE -ne 0) {
    throw "Terrias semantic architecture boundary validation failed."
}

$projectPath = Join-Path $repoRoot "Terrias-Dev\Terrias.Dll.csproj"
[xml]$project = Get-Content -Raw -LiteralPath $projectPath
$projectReferences = @($project.Project.ItemGroup.ProjectReference | ForEach-Object { [string]$_.Include })
if ($projectReferences -notcontains "..\AuraSharedRuntime-Dev\Aura.Shared.csproj") {
    throw "Terrias must reference the shared runtime project."
}
if (@($projectReferences | Where-Object { $_ -match "AuraToolsExp|SanGuoShaExp|TestMods" }).Count -gt 0) {
    throw "Terrias must not reference a sibling product or archived prototype project."
}

$productionLua = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "Terrias") -Recurse -File -Filter "*.lua")
if ($productionLua.Count -gt 0) {
    throw "Terrias production behavior must stay in C#; unexpected Lua file(s): $($productionLua.FullName -join ', ')"
}

$managedTarget = [regex]'CS\.Terrias\.Dll\.([A-Za-z0-9_\.]+)'
$dataRoot = Join-Path $repoRoot "Terrias\Data"
foreach ($file in (Get-ChildItem -LiteralPath $dataRoot -Recurse -File -Filter "*.csv")) {
    $rows = @(Import-Csv -LiteralPath $file.FullName)
    foreach ($row in $rows) {
        foreach ($property in $row.PSObject.Properties) {
            if ($property.Name -notmatch "Script" -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
                continue
            }
            foreach ($match in $managedTarget.Matches([string]$property.Value)) {
                if (-not $match.Groups[1].Value.StartsWith("Scripting.", [System.StringComparison]::Ordinal)) {
                    $relative = [System.IO.Path]::GetRelativePath($repoRoot, $file.FullName)
                    throw "CSV managed target must route through Terrias.Dll.Scripting: $relative -> $($match.Value)"
                }
            }
        }
    }
}

$dialoguePath = Join-Path $repoRoot "Terrias\Data\Dialogue\terrias.csv"
if ((Get-Content -Raw -LiteralPath $dialoguePath) -match 'CS\.Terrias\.Dll\.Scripting') {
    throw "Native Dialogue script columns must not call managed Terrias scripting entry points."
}

Write-Host "Terrias architecture assertions passed."
