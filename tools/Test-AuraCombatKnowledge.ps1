param()

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$scripts = Get-ChildItem -LiteralPath $root -Filter "AllScripts.cs" -File -Recurse |
    Where-Object { $_.FullName -match 'v1\.0\.24591395[\\/]AllScripts[\\/](?:AllScripts[\\/])?AllScripts\.cs$' } |
    Select-Object -First 1 -ExpandProperty FullName
$tables = Get-ChildItem -LiteralPath (Join-Path $root "docs") -Filter "witch-tables-20260806-172142.json" -File -Recurse |
    Select-Object -First 1 -ExpandProperty FullName
$output = Join-Path ([System.IO.Path]::GetTempPath()) "aura-combat-knowledge-contract.json"
$report = Join-Path ([System.IO.Path]::GetTempPath()) "aura-combat-knowledge-contract.report.json"

if ([string]::IsNullOrWhiteSpace($scripts) -or -not (Test-Path -LiteralPath $scripts -PathType Leaf)) {
    $bundled = Join-Path $root "AuraToolsExp\Config\combat-knowledge.base-game.json"
    if (-not (Test-Path -LiteralPath $bundled -PathType Leaf)) {
        throw "Neither the decompiled source fixture nor the bundled combat knowledge package exists."
    }
    $package = Get-Content -LiteralPath $bundled -Raw | ConvertFrom-Json
    if ($package.gameBuild -ne "1.0.24591395" `
        -or $package.sourceHash -ne "1e4859af3d987bccb1019d85619dbeb9c1e0c23379275c4ebd5e48b0b94906f2" `
        -or $package.actions.Count -ne 932 `
        -or $package.statuses.Count -ne 137 `
        -or $package.enemies.Count -ne 56 `
        -or $package.encounters.Count -ne 50) {
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
    --game-build "1.0.24591395"
if ($LASTEXITCODE -ne 0) {
    throw "Aura combat knowledge compiler failed."
}

$package = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
$compilerReport = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
$card = $package.actions | Where-Object sourceId -eq "elementscard_1" | Select-Object -First 1
$enemy = $package.enemies | Where-Object enemyId -eq "enemy_10001" | Select-Object -First 1
$encounter = $package.encounters | Where-Object encounterId -eq "level_10001" | Select-Object -First 1
$expectedCardName = -join @([char]0x6D77, [char]0x6D0B, [char]0x4E4B, [char]0x68A6)

if ($compilerReport.gameBuild -ne "1.0.24591395" `
    -or $compilerReport.sourceHash -ne "1e4859af3d987bccb1019d85619dbeb9c1e0c23379275c4ebd5e48b0b94906f2" `
    -or $compilerReport.registeredScriptCount -ne 3187 `
    -or $compilerReport.operationCount -ne 3444 `
    -or $compilerReport.unsupportedOperationCount -ne 535 `
    -or $compilerReport.parseDiagnostics.Count -ne 0 `
    -or $package.actions.Count -ne 932 `
    -or $package.statuses.Count -ne 137 `
    -or $package.enemies.Count -ne 56 `
    -or $package.encounters.Count -ne 50 `
    -or $null -eq $card `
    -or $card.displayName -ne $expectedCardName `
    -or $card.baseCost -ne 0 `
    -or $null -eq $enemy `
    -or $enemy.maxHp -ne 40 `
    -or $null -eq $encounter `
    -or $encounter.enemyIds[0] -ne "enemy_10004") {
    throw "Aura combat knowledge compiler contract is invalid."
}

Write-Host "Aura combat knowledge compiler contract passed."
