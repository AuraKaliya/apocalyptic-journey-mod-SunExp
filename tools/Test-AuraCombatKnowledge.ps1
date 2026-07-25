param()

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$scripts = Join-Path $root "开发参考资料\反编译文件夹v1.0.23816797\AllScripts\AllScripts.cs"
$tables = Join-Path $root "docs\AuraCombatAI\examples\knowledge-table-export.example.json"
$output = Join-Path ([System.IO.Path]::GetTempPath()) "aura-combat-knowledge-contract.json"
$report = Join-Path ([System.IO.Path]::GetTempPath()) "aura-combat-knowledge-contract.report.json"

if (-not (Test-Path -LiteralPath $scripts -PathType Leaf)) {
    $bundled = Join-Path $root "AuraToolsExp\Config\combat-knowledge.base-game.json"
    if (-not (Test-Path -LiteralPath $bundled -PathType Leaf)) {
        throw "Neither the decompiled source fixture nor the bundled combat knowledge package exists."
    }
    $package = Get-Content -LiteralPath $bundled -Raw | ConvertFrom-Json
    if ($package.gameBuild -ne "1.0.23816797" `
        -or $package.actions.Count -lt 870 `
        -or $package.statuses.Count -lt 80 `
        -or $package.enemies.Count -lt 56) {
        throw "Bundled combat knowledge package contract is invalid."
    }
    Write-Host "Aura combat knowledge bundled-package contract passed (source fixture unavailable)."
    return
}

dotnet run `
    --project (Join-Path $root "tools\AuraCombatKnowledgeCompiler\AuraCombatKnowledgeCompiler.csproj") `
    -c Release `
    -- `
    --scripts $scripts `
    --tables $tables `
    --output $output `
    --report $report `
    --game-build "1.0.23816797"
if ($LASTEXITCODE -ne 0) {
    throw "Aura combat knowledge compiler failed."
}

$package = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
$compilerReport = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
$card = $package.actions | Where-Object sourceId -eq "elementscard_1" | Select-Object -First 1
$enemy = $package.enemies | Where-Object enemyId -eq "enemy_example" | Select-Object -First 1
$encounter = $package.encounters | Where-Object encounterId -eq "level_example" | Select-Object -First 1

if ($compilerReport.registeredScriptCount -lt 2900 `
    -or $compilerReport.parseDiagnostics.Count -ne 0 `
    -or $package.statuses.Count -lt 80 `
    -or $package.enemies.Count -lt 56 `
    -or $null -eq $card `
    -or $card.displayName -ne "海洋之梦" `
    -or $card.baseCost -ne 0 `
    -or $null -eq $enemy `
    -or $enemy.maxHp -ne 30 `
    -or $null -eq $encounter `
    -or $encounter.enemyIds[0] -ne "enemy_example") {
    throw "Aura combat knowledge compiler contract is invalid."
}

Write-Host "Aura combat knowledge compiler contract passed."
