param(
    [Parameter(Mandatory = $true)]
    [string]$TableExport,

    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\AuraToolsExp\Config\combat-simulation")
)

$ErrorActionPreference = "Stop"

function Convert-ToInt([object]$value, [int]$fallback = 0) {
    $parsed = 0
    if ([int]::TryParse([string]$value, [ref]$parsed)) {
        return $parsed
    }
    return $fallback
}

function Convert-ToDouble([object]$value, [double]$fallback = 0.0) {
    $parsed = 0.0
    if ([double]::TryParse(
        [string]$value,
        [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::InvariantCulture,
        [ref]$parsed)) {
        return $parsed
    }
    return $fallback
}

function Get-FirstNumber([string]$text, [int]$fallback) {
    $source = if ($null -eq $text) { "" } else { $text }
    $match = [regex]::Match($source, "(?<![A-Za-z_])\d+")
    if ($match.Success) {
        return [Math]::Max(1, (Convert-ToInt $match.Value $fallback))
    }
    return $fallback
}

function Test-BaseGameId([string]$id) {
    return -not [string]::IsNullOrWhiteSpace($id) `
        -and $id -notmatch "(?i)Terrias|^Saya_|(^|_)test|99999"
}

function Get-RewardFeatures([object]$row, [string]$kind) {
    $text = "$($row.Name) $($row.Name_en) $($row.Description) $($row.Description_en) $($row.Effects) $($row.Action) $($row.UseScript) $($row.OwnScript) $($row.FightScript)"
    $features = [ordered]@{
        burst = 0.0
        sustained = 0.0
        defense = 0.0
        heal = 0.0
        aoe = 0.0
        cycling = 0.0
        energy = 0.0
        reliability = 0.65
        risk = 0.0
    }
    if ($text -match "(?i)damage|伤害|流血|燃烧|Damage|AddHurt") {
        $features.burst = 0.7
        $features.sustained = 0.35
    }
    if ($text -match "(?i)block|shield|护盾|格挡|装甲|Defend") {
        $features.defense = 0.8
    }
    if ($text -match "(?i)heal|restore health|恢复.*生命|回复.*生命|ChangeHp") {
        $features.heal = 0.8
    }
    if ($text -match "(?i)all enem|所有敌|全体敌") {
        $features.aoe = 0.8
    }
    if ($text -match "(?i)draw|抽.*牌|DrawCard") {
        $features.cycling = 0.7
    }
    if ($text -match "(?i)energy|魔能|行动力") {
        $features.energy = 0.7
    }
    if ($text -match "(?i)lose health|失去.*生命|弃牌|exhaust|消耗|随机") {
        $features.risk = 0.45
    }
    if ($kind -eq "Relic") {
        $features.reliability = 0.75
    }
    if ($kind -eq "Blessing") {
        $features.reliability = 0.9
    }
    return $features
}

function Get-PermanentAttributeBonuses([object]$row) {
    $result = [ordered]@{}
    $script = [string]$row.OwnScript
    foreach ($attribute in @("Strength", "Lucky", "Perceive", "Wisdom")) {
        $total = 0
        $direct = [regex]::Matches(
            $script,
            "PlayerInfo\.$attribute\s*\+=\s*(\d+)",
            [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        foreach ($match in $direct) {
            $total += Convert-ToInt $match.Groups[1].Value 0
        }
        $expanded = [regex]::Matches(
            $script,
            "PlayerInfo\.$attribute\s*=\s*PlayerInfo\.$attribute\s*\+\s*(\d+)",
            [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        foreach ($match in $expanded) {
            $total += Convert-ToInt $match.Groups[1].Value 0
        }
        if ($total -ne 0) {
            $result[$attribute] = $total
        }
    }
    return $result
}

function Get-MaxHpBonus([object]$row) {
    $script = [string]$row.OwnScript
    $total = 0
    foreach ($match in [regex]::Matches(
        $script,
        "PlayerInfo\.MaxHp\s*\+=\s*(\d+)",
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        $total += Convert-ToInt $match.Groups[1].Value 0
    }
    return $total
}

function New-ApproximateCard([object]$row) {
    $description = "$($row.Description) $($row.Description_en) $($row.UseScript)"
    $amount = Get-FirstNumber $description 6
    $target = if ($description -match "(?i)all enem|所有敌|全体敌") { "AllEnemies" } else { "SelectedEnemy" }
    $effects = @()
    if ($description -match "(?i)heal|restore health|恢复.*生命|回复.*生命|ChangeHp") {
        $effects += [ordered]@{ kind = "Heal"; target = "Self"; amount = [Math]::Min(99, $amount) }
    }
    if ($description -match "(?i)block|shield|护盾|格挡|装甲|Defend") {
        $effects += [ordered]@{ kind = "GainBlock"; target = "Self"; amount = [Math]::Min(99, $amount) }
    }
    if ($description -match "(?i)damage|伤害|流血|燃烧|Hurt|AddBuff" -or $effects.Count -eq 0) {
        $effects += [ordered]@{ kind = "Damage"; target = $target; amount = [Math]::Min(99, $amount) }
    }
    return [ordered]@{
        ownerModId = "Witch"
        cardId = [string]$row.Id
        displayName = [string]$row.Name
        cost = [Math]::Max(0, [Math]::Min(9, (Convert-ToInt $row.Expend 1)))
        exhaust = "$($row.UseScript) $($row.InitScript)" -match "(?i)exhaust|消耗|RemoveCard"
        requiresEnemyTarget = $effects.Where({ $_.target -eq "SelectedEnemy" }).Count -gt 0
        fidelity = "Approximate"
        effects = @($effects)
    }
}

function New-ApproximateEnemy([object]$row) {
    $attack = [Math]::Max(1, (Convert-ToInt $row.Attack 5))
    $block = [Math]::Max(0, (Convert-ToInt $row.Defend 0))
    $intents = @(
        [ordered]@{
            intentId = "table-basic-attack"
            displayName = "本体表基础攻击（近似）"
            weight = 3
            effects = @([ordered]@{ kind = "Damage"; target = "Player"; amount = $attack })
        }
    )
    if ($block -gt 0) {
        $intents += [ordered]@{
            intentId = "table-basic-block"
            displayName = "本体表基础防御（近似）"
            weight = 1
            effects = @([ordered]@{ kind = "GainBlock"; target = "Self"; amount = $block })
        }
    }
    return [ordered]@{
        ownerModId = "Witch"
        enemyId = [string]$row.Id
        displayName = [string]$row.Name
        maxHp = [Math]::Max(1, (Convert-ToInt $row.Hp 1))
        fidelity = "Approximate"
        intents = @($intents)
    }
}

$resolvedExport = (Resolve-Path -LiteralPath $TableExport).Path
$tables = (Get-Content -Raw -Encoding UTF8 -LiteralPath $resolvedExport | ConvertFrom-Json).tables
$levels = @($tables.Level | Where-Object { Test-BaseGameId ([string]$_.Id) })
$enemies = @($tables.Enemy | Where-Object { Test-BaseGameId ([string]$_.Id) })
$cards = @($tables.Card | Where-Object { Test-BaseGameId ([string]$_.Id) })
$relics = @($tables.Relic | Where-Object { Test-BaseGameId ([string]$_.Id) })
$blessings = @($tables.Bless | Where-Object { Test-BaseGameId ([string]$_.Id) })

$route = @("Normal", "Normal", "Elite", "Normal", "Normal", "Boss")
$attributePresets = @(
    @(10, 7, 5),
    @(20, 10, 7),
    @(25, 15, 10),
    @(30, 20, 15),
    @(35, 30, 17),
    @(35, 35, 20),
    @(40, 39, 20)
)
$layers = for ($index = 0; $index -lt 7; $index++) {
    $layerRoute = if ($index -lt 6) { @($route) } else { @("FinalBoss") }
    [ordered]@{
        layerNumber = $index + 1
        nativeBand = if ($index -lt 6) { [Math]::Floor($index / 2) } else { 3 }
        attributes = [ordered]@{
            main = $attributePresets[$index][0]
            secondary = $attributePresets[$index][1]
            unselected = $attributePresets[$index][2]
        }
        route = @($layerRoute)
        maxHpGainAfterClear = if ($index -lt 6) { 40 } else { 0 }
    }
}

$encounters = @()
foreach ($level in $levels) {
    $band = Convert-ToInt $level.Level -99
    if ($band -notin @(-1, 0, 1, 2)) {
        continue
    }
    $note = ([string]$level.Note).Trim().ToLowerInvariant()
    $kind = switch -Regex ($note) {
        "精英" { "Elite"; break }
        "boss|首领|領主" { "Boss"; break }
        "普通" { "Normal"; break }
        default { ""; break }
    }
    if ([string]::IsNullOrWhiteSpace($kind)) {
        continue
    }
    $enemyIds = @(([string]$level.EnemyIds -split ",") |
        ForEach-Object { $_.Trim() } |
        Where-Object { Test-BaseGameId $_ })
    if ($enemyIds.Count -eq 0) {
        continue
    }
    $encounters += [ordered]@{
        encounterId = [string]$level.Id
        kind = $kind
        nativeBand = $band
        enemyIds = $enemyIds
    }
}
$encounters += @(
    [ordered]@{
        encounterId = "final-caroline-perfect-angel"
        kind = "FinalBoss"
        nativeBand = 3
        enemyIds = @("enemy_10058")
    },
    [ordered]@{
        encounterId = "final-evernight-incarnation"
        kind = "FinalBoss"
        nativeBand = 3
        enemyIds = @("enemy_10027")
    },
    [ordered]@{
        encounterId = "final-demon-king"
        kind = "FinalBoss"
        nativeBand = 3
        enemyIds = @("enemy_10049", "enemy_10048", "enemy_10050")
    },
    [ordered]@{
        encounterId = "final-holy-judgment-engine"
        kind = "FinalBoss"
        nativeBand = 3
        enemyIds = @("enemy_10055")
    }
)

$rewardDefinitions = @()
foreach ($card in $cards) {
    $tier = [Math]::Max(1, [Math]::Min(4, (Convert-ToInt $card.Rarity 1)))
    $rewardDefinitions += [ordered]@{
        rewardId = [string]$card.Id
        kind = "Card"
        tier = $tier
        baseValue = 0.55 + $tier * 0.2
        negative = $false
        fidelity = "Approximate"
        features = Get-RewardFeatures $card "Card"
    }
}
foreach ($relic in $relics) {
    $tier = [Math]::Max(1, [Math]::Min(4, (Convert-ToInt $relic.Rarity 1)))
    $rewardDefinitions += [ordered]@{
        rewardId = [string]$relic.Id
        kind = "Relic"
        tier = $tier
        baseValue = 0.7 + $tier * 0.28
        negative = $false
        fidelity = "Approximate"
        features = Get-RewardFeatures $relic "Relic"
        permanentAttributeBonuses = Get-PermanentAttributeBonuses $relic
        maxHpBonus = Get-MaxHpBonus $relic
    }
}
foreach ($blessing in $blessings) {
    $tier = [Math]::Max(1, [Math]::Min(4, (Convert-ToInt $blessing.Rarity 1)))
    $negative = ([string]$blessing.Type) -match "负面|負面|negative"
    $rewardDefinitions += [ordered]@{
        rewardId = [string]$blessing.Id
        kind = "Blessing"
        tier = $tier
        baseValue = 0.65 + $tier * 0.25
        negative = $negative
        fidelity = "Approximate"
        features = Get-RewardFeatures $blessing "Blessing"
        permanentAttributeBonuses = Get-PermanentAttributeBonuses $blessing
        maxHpBonus = Get-MaxHpBonus $blessing
    }
}

$hardAffixes = @($tables.Hard |
    Where-Object { Test-BaseGameId ([string]$_.Id) } |
    ForEach-Object {
        $id = [string]$_.Id
        $combatRelevant = $id -in @(
            "Hard_3", "Hard_4", "Hard_5", "Hard_7", "Hard_8", "Hard_9",
            "Hard_10", "Hard_13", "Hard_14", "Hard_15", "Hard_18", "Hard_19",
            "Hard_20", "Hard_22")
        [ordered]@{
            affixId = $id
            stacks = 1
            combatRelevant = $combatRelevant
            implemented = $id -in @(
                "Hard_1", "Hard_2", "Hard_3", "Hard_4", "Hard_6", "Hard_9",
                "Hard_10", "Hard_11", "Hard_14", "Hard_16", "Hard_17", "Hard_18",
                "Hard_21")
        }
    })

$campaign = [ordered]@{
    schemaVersion = 2
    campaignId = "witch.world-simulation.standard-v2"
    campaignVersion = "2.0.0"
    rulesetVersion = "witch-base-evaluation-v2"
    player = [ordered]@{
        roleId = "career_1"
        maxHp = 20
        currentHp = 20
        baseEnergy = 3
        deck = @(
            "card_1", "card_1", "card_1", "card_1",
            "card_2", "card_2", "card_2", "card_2",
            "card_4", "card_6", "card_14")
        initialStatuses = @()
        variables = [ordered]@{}
    }
    mainAttributeId = "Strength"
    secondaryAttributeId = "Wisdom"
    attributeIds = @("Strength", "Lucky", "Perceive", "Wisdom")
    mainAttributeUpperBound = 40
    secondaryAttributeUpperBound = 39
    unselectedAttributeUpperBound = 20
    layers = @($layers)
    enemies = @($enemies | ForEach-Object {
        [ordered]@{
            enemyId = [string]$_.Id
            nativeLevel = [Math]::Max(1, (Convert-ToInt $_.Level 1))
        }
    })
    encounters = @($encounters)
    rewards = @($rewardDefinitions)
    difficulties = @(
        [ordered]@{
            difficultyId = "normal"
            displayName = "普通难度"
            enemyHpMultiplier = 1.0
            enemyAttackMultiplier = 1.0
            applyGameLevelShield = $false
            enemyInitialStatuses = @()
            hardAffixes = @()
        },
        [ordered]@{
            difficultyId = "advanced"
            displayName = "高级难度（本体满词条）"
            enemyHpMultiplier = 1.1
            enemyAttackMultiplier = 1.1
            applyGameLevelShield = $true
            enemyInitialStatuses = @()
            hardAffixes = $hardAffixes
        }
    )
    cardOfferRounds = 2
    cardChoicesPerRound = 3
    relicLimit = 6
    allowSkipCardReward = $true
    blessingsAreMandatory = $true
    excludeNegativeBlessings = $true
    rewardAfterFinalBoss = $false
    rolePrior = [ordered]@{
        burst = 0.55; sustained = 0.45; defense = 0.6; heal = 0.45
        aoe = 0.2; cycling = 0.35; energy = 0.45; reliability = 0.8; risk = -0.7
    }
    buildTendency = [ordered]@{
        burst = 0.45; sustained = 0.55; defense = 0.55; heal = 0.5
        aoe = 0.15; cycling = 0.4; energy = 0.4; reliability = 0.75; risk = -0.8
    }
    bossPreference = [ordered]@{
        burst = 0.5; sustained = 0.8; defense = 0.75; heal = 0.6
        aoe = 0.0; cycling = 0.35; energy = 0.4; reliability = 0.85; risk = -0.9
    }
    initialDraw = 5
    drawPerTurn = 5
    handLimit = 10
    retainBlockBetweenTurns = $false
    requireAuthoritativeRules = $false
    traceLevel = "Summary"
    limits = [ordered]@{
        maximumTurns = 100
        maximumActions = 5000
        maximumCommands = 50000
        maximumCommandsPerAction = 1000
        maximumTriggerWavesPerAction = 100
        maximumSummonedActors = 16
    }
}

$v1RulesetPath = Join-Path $OutputDirectory "witch-base-evaluation-v1.ruleset.json"
$v1Ruleset = Get-Content -Raw -Encoding UTF8 -LiteralPath $v1RulesetPath | ConvertFrom-Json
$authoritativeCards = @{}
foreach ($card in $v1Ruleset.cards) {
    $authoritativeCards[[string]$card.cardId] = $card
}
$authoritativeEnemies = @{}
foreach ($enemy in $v1Ruleset.enemies) {
    $authoritativeEnemies[[string]$enemy.enemyId] = $enemy
}
$rulesetCards = @($cards | ForEach-Object {
    if ($authoritativeCards.ContainsKey([string]$_.Id)) {
        $authoritativeCards[[string]$_.Id]
    } else {
        New-ApproximateCard $_
    }
})
$rulesetEnemies = @($enemies | ForEach-Object {
    if ($authoritativeEnemies.ContainsKey([string]$_.Id)) {
        $authoritativeEnemies[[string]$_.Id]
    } else {
        New-ApproximateEnemy $_
    }
})
$ruleset = [ordered]@{
    version = "witch-base-evaluation-v2"
    cards = $rulesetCards
    enemies = $rulesetEnemies
    statuses = @()
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$campaignPath = Join-Path $OutputDirectory "witch-world-simulation-v2.campaign.json"
$rulesetPath = Join-Path $OutputDirectory "witch-base-evaluation-v2.ruleset.json"
[IO.File]::WriteAllText(
    $campaignPath,
    (($campaign | ConvertTo-Json -Depth 30).Replace("`r`n", "`n")),
    $utf8)
[IO.File]::WriteAllText(
    $rulesetPath,
    (($ruleset | ConvertTo-Json -Depth 30).Replace("`r`n", "`n")),
    $utf8)

Write-Host "Campaign: $campaignPath"
Write-Host "Ruleset: $rulesetPath"
Write-Host "Pools: $($encounters.Count) encounters, $($enemies.Count) enemies"
Write-Host "Rewards: $($cards.Count) cards, $($relics.Count) relics, $($blessings.Count) blessings"
