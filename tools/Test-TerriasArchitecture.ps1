param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Import-Module (Join-Path $repoRoot "tools\modules\ArchitectureBoundaryValidation.psm1") -Force
Invoke-ArchitectureBoundaryValidation -RepoRoot $repoRoot -RuleSet "terrias"

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

$combatCardPoolSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "Terrias-Dev\Hooks\Ui\TerriasCombatCardViewPool.cs")
if ($combatCardPoolSource -notmatch 'AuraCardPresentationRuntime\.RequestApply' `
        -or $combatCardPoolSource -notmatch 'AuraCardPresentationRuntime\.RequestReset' `
        -or $combatCardPoolSource -match 'TerriasCardPresentationRouter\.RequestApply' `
        -or $combatCardPoolSource -notmatch 'OutcomeEntering\s*=\s*_\s*=>\s*EndFight' `
        -or $combatCardPoolSource -notmatch 'BattleSettling\s*=\s*_\s*=>\s*EndFight' `
        -or $combatCardPoolSource -notmatch 'BattleRestarting\s*=\s*_\s*=>\s*EndFight' `
        -or $combatCardPoolSource -notmatch 'BattleEnded\s*=\s*_\s*=>\s*EndFight' `
        -or $combatCardPoolSource -notmatch 'TeardownHandPresentation') {
    throw "Terrias pooled combat cards must use shared reset/apply and idempotently clear the hand before settlement."
}

Write-Host "Terrias architecture assertions passed."
