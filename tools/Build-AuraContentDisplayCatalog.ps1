param(
    [Parameter(Mandatory = $true)]
    [string]$TableExport,

    [string]$CampaignPath = "",

    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($CampaignPath)) {
    $CampaignPath = Join-Path $PSScriptRoot `
        "..\AuraToolsExp\Config\combat-simulation\witch-world-simulation-v2.campaign.json"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot `
        "..\AuraToolsExp\Config\combat-simulation\witch-content-display-catalog-v1.json"
}

function Get-TextSha256([string]$text) {
    $bytes = [Text.Encoding]::UTF8.GetBytes(
        $(if ($null -eq $text) { "" } else { $text }))
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $sha.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

function Add-Entry(
    [Collections.Generic.List[object]]$entries,
    [string]$type,
    [object]$row,
    [string]$nameProperty,
    [string]$descriptionProperty,
    [string]$sourceTable) {
    $id = ([string]$row.Id).Trim()
    $name = ([string]$row.$nameProperty).Trim()
    if ([string]::IsNullOrWhiteSpace($id) `
        -or [string]::IsNullOrWhiteSpace($name)) {
        return
    }
    $description = if ([string]::IsNullOrWhiteSpace($descriptionProperty)) {
        ""
    } else {
        ([string]$row.$descriptionProperty).Trim()
    }
    $sourceJson = $row | ConvertTo-Json -Depth 12 -Compress
    $entries.Add([ordered]@{
        ownerModId = "Witch"
        entityType = $type
        entityId = $id
        displayName = $name
        description = $description
        source = "witch-table-export:$sourceTable"
        sourceHash = Get-TextSha256 $sourceJson
    })
}

$resolvedExport = (Resolve-Path -LiteralPath $TableExport).Path
$document = Get-Content -Raw -Encoding UTF8 -LiteralPath $resolvedExport |
    ConvertFrom-Json
$tables = $document.Tables
$entries = [Collections.Generic.List[object]]::new()

foreach ($row in @($tables.Card)) {
    Add-Entry $entries "card" $row "Name" "Description" "Card"
}
foreach ($row in @($tables.Relic)) {
    Add-Entry $entries "relic" $row "Name" "Description" "Relic"
}
foreach ($row in @($tables.Bless)) {
    Add-Entry $entries "blessing" $row "Name" "Description" "Bless"
}
foreach ($row in @($tables.Enemy)) {
    Add-Entry $entries "enemy" $row "Name" "Description1" "Enemy"
}
foreach ($row in @($tables.Buff)) {
    Add-Entry $entries "buff" $row "Name" "Description" "Buff"
}

$enemyNames = @{}
foreach ($row in @($tables.Enemy)) {
    $enemyNames[[string]$row.Id] = [string]$row.Name
}
$campaign = Get-Content -Raw -Encoding UTF8 -LiteralPath $CampaignPath |
    ConvertFrom-Json
foreach ($encounter in @($campaign.Encounters)) {
    $names = @($encounter.EnemyIds | ForEach-Object {
        if ($enemyNames.ContainsKey([string]$_)) {
            $enemyNames[[string]$_]
        } else {
            [string]$_
        }
    })
    $kind = switch ([string]$encounter.Kind) {
        "Normal" { "普通战斗" }
        "Elite" { "精英战斗" }
        "Boss" { "首领战斗" }
        "FinalBoss" { "最终首领" }
        default { "战斗" }
    }
    $sourceJson = $encounter | ConvertTo-Json -Depth 8 -Compress
    $entries.Add([ordered]@{
        ownerModId = "Witch"
        entityType = "encounter"
        entityId = [string]$encounter.EncounterId
        displayName = if ($names.Count -gt 0) {
            ($names -join "、") + "（" + $kind + "）"
        } else {
            [string]$encounter.EncounterId
        }
        description = "敌人：" + ($names -join "、")
        source = "witch-campaign:Encounters"
        sourceHash = Get-TextSha256 $sourceJson
    })
}

$catalog = [ordered]@{
    schemaVersion = 1
    catalogId = "witch-content-display-zh-cn-v1"
    locale = "zh-CN"
    gameBuild = [string]$document.GameBuild
    exportedAtUtc = [DateTime]$document.ExportedAtUtc
    entries = @($entries | Sort-Object entityType, entityId)
}

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText(
    $OutputPath,
    (($catalog | ConvertTo-Json -Depth 12).Replace("`r`n", "`n")),
    $utf8)
Write-Host "Content display catalog: $OutputPath ($($entries.Count) entries)"
