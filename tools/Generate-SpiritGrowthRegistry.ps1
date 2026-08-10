param(
    [string]$SpeciesTable = (Join-Path $PSScriptRoot '..\docs\Terrias\design\04-游戏主体精灵种族值表.csv'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\Terrias\spirit.growth.registry.json')
)

$ErrorActionPreference = 'Stop'

function New-Origin($Magic, $Spirit, $Luck, $Perception) {
    [ordered]@{
        magic = [int]$Magic
        spirit = [int]$Spirit
        luck = [int]$Luck
        perception = [int]$Perception
    }
}

$formMetadata = @{
    '10036' = @('base-game.lesser-joker', 'black', 0, 'form.black')
    '10037' = @('base-game.lesser-joker', 'white', 1, 'form.white')
    '10053' = @('base-game.evernight-incarnation', 'derived', 0, 'form.derived')
    '10027' = @('base-game.evernight-incarnation', 'final', 1, 'form.final')
    '10049' = @('base-game.demon-king-sword', 'right', 0, 'form.right')
    '10050' = @('base-game.demon-king-sword', 'left', 1, 'form.left')
    '10048' = @('base-game.demon-king', 'phase-1', 1, 'form.phase-1')
    '10051' = @('base-game.demon-king', 'phase-2', 2, 'form.phase-2')
    '10052' = @('base-game.demon-king', 'phase-3', 3, 'form.phase-3')
    '10056' = @('base-game.caroline', 'phase-1', 1, 'form.phase-1')
    '10057' = @('base-game.caroline', 'phase-2', 2, 'form.phase-2')
    '10058' = @('base-game.caroline', 'complete-angel', 3, 'form.complete-angel')
}

$curveIds = [ordered]@{
    level = 'level-linear-1-50'
    roll = 'aptitude-roll-normal-60-15'
    aptitude = 'aptitude-smoothstep-080-120'
    experience = 'xp-standard-1-50'
    conversion = 'origins-battle-standard-v1'
    radar = 'origins-global-v1'
}

function New-Profile(
    [string]$SpeciesId,
    [string]$ProfileId,
    [string]$FormKey,
    [int]$FormOrder,
    [string]$FormLabelKey,
    [string]$SourceModId,
    [string]$EnemyId,
    [string]$VariantId,
    [string]$Tier,
    $BaseOrigins,
    $GrowthOrigins
) {
    [ordered]@{
        speciesId = $SpeciesId
        profileId = $ProfileId
        formKey = $FormKey
        formOrder = $FormOrder
        formLabelKey = $FormLabelKey
        match = [ordered]@{
            sourceModId = $SourceModId
            enemyId = $EnemyId
            variantId = $VariantId
        }
        tier = $Tier
        baseOrigins = $BaseOrigins
        growthOrigins = $GrowthOrigins
        levelCurveId = $curveIds.level
        aptitudeRollProfileId = $curveIds.roll
        aptitudeCurveId = $curveIds.aptitude
        experienceCurveId = $curveIds.experience
        battleConversionId = $curveIds.conversion
        radarScaleId = $curveIds.radar
    }
}

if (-not (Test-Path -LiteralPath $SpeciesTable)) { throw "Species table not found: $SpeciesTable" }
$profiles = [System.Collections.Generic.List[object]]::new()
foreach ($row in @(Import-Csv -LiteralPath $SpeciesTable)) {
    $enemyId = ([string]$row.enemyId).Trim()
    $profileId = ([string]$row.profileId).Trim()
    $metadata = $formMetadata[$enemyId]
    $speciesId = if ($null -eq $metadata) { $profileId } else { [string]$metadata[0] }
    $formKey = if ($null -eq $metadata) { 'default' } else { [string]$metadata[1] }
    $formOrder = if ($null -eq $metadata) { 0 } else { [int]$metadata[2] }
    $formLabelKey = if ($null -eq $metadata) { 'form.default' } else { [string]$metadata[3] }
    $profiles.Add((New-Profile $speciesId $profileId $formKey $formOrder $formLabelKey 'base-game' $enemyId '*' ([string]$row.tier) `
        (New-Origin $row.baseMagic $row.baseSpirit $row.baseLuck $row.basePerception) `
        (New-Origin $row.growthMagic $row.growthSpirit $row.growthLuck $row.growthPerception)))
}

$profiles.Add((New-Profile 'terrias.boss_orbit_mirror_array' 'terrias.boss_orbit_mirror_array' 'default' 0 'form.default' 'terrias' 'boss_orbit_mirror_array' '*' 'Boss' `
    (New-Origin 13 13 8 10) (New-Origin 28 30 18 24)))
$profiles.Add((New-Profile 'terrias.boss_second_sun_last_day' 'terrias.boss_second_sun_last_day' 'default' 0 'form.default' 'terrias' 'boss_second_sun_last_day' '*' 'FinalBoss' `
    (New-Origin 17 16 9 12) (New-Origin 36 35 20 29)))
$profiles.Add((New-Profile 'terrias.boss_saint_wuna' 'terrias.boss_saint_wuna' 'default' 0 'form.default' 'terrias' 'boss_saint_wuna' '*' 'FinalBoss' `
    (New-Origin 18 18 8 10) (New-Origin 38 38 18 26)))

$document = [ordered]@{
    schemaVersion = 2
    defaults = [ordered]@{
        maxLevel = 50
        levelCurveId = $curveIds.level
        aptitudeRollProfileId = $curveIds.roll
        aptitudeCurveId = $curveIds.aptitude
        experienceCurveId = $curveIds.experience
        battleConversionId = $curveIds.conversion
        radarScaleId = $curveIds.radar
    }
    formLabels = [ordered]@{
        'form.default' = ''
        'form.black' = '黑色形态'
        'form.white' = '白色形态'
        'form.derived' = '派生形态'
        'form.final' = '最终形态'
        'form.right' = '右剑'
        'form.left' = '左剑'
        'form.phase-1' = '第一形态'
        'form.phase-2' = '第二形态'
        'form.phase-3' = '第三形态'
        'form.complete-angel' = '完全天使'
    }
    levelCurves = @(
        [ordered]@{ id = $curveIds.level; type = 'normalizedLinear'; minLevel = 1; maxLevel = 50 }
    )
    aptitudeRollProfiles = @(
        [ordered]@{ id = $curveIds.roll; type = 'truncatedNormal'; mean = 60.0; standardDeviation = 15.0; minimum = 0; maximum = 100; fallback = 60; maximumAttempts = 64 }
    )
    aptitudeCurves = @(
        [ordered]@{ id = $curveIds.aptitude; type = 'smoothstep'; inputMin = 0; inputMax = 100; outputMin = 0.8; outputMax = 1.2 }
    )
    experienceCurves = @(
        [ordered]@{ id = $curveIds.experience; type = 'quadraticStep'; base = 20; linear = 2; quadraticDivisor = 24 }
    )
    battleConversions = @(
        [ordered]@{
            id = $curveIds.conversion
            hpBase = 20.0; hpSpirit = 2.4; hpLuck = 0.8
            attackBase = 3.0; attackMagic = 0.8; attackPerception = 0.25; attackLuck = 0.15
            armorBase = 1.0; armorPerception = 0.55; armorSpirit = 0.2; armorLuck = 0.1
            intentEnergyBase = 3.0; intentEnergyMagic = 0.15; intentEnergyPerception = 0.1
        }
    )
    radarScaleSets = @(
        [ordered]@{
            id = $curveIds.radar
            mode = 'absoluteCaps'
            axes = @(
                [ordered]@{ key = 'magic'; cap = 80 },
                [ordered]@{ key = 'perception'; cap = 80 },
                [ordered]@{ key = 'spirit'; cap = 80 },
                [ordered]@{ key = 'luck'; cap = 80 }
            )
        }
    )
    profiles = @($profiles | Sort-Object { [string]$_.profileId })
}

$directory = Split-Path -Parent $OutputPath
if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
$json = ($document | ConvertTo-Json -Depth 12) -replace "`r`n", "`n"
[IO.File]::WriteAllText($OutputPath, $json + "`n", [Text.UTF8Encoding]::new($false))
Write-Host "Generated schema 2 spirit growth registry: profiles=$($profiles.Count), path=$OutputPath"
