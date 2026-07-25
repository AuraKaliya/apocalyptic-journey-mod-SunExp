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
    $tags = @(([string]$row.Tag -split "\||,|，|;|；") |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    if ("$($row.Description) $($row.Description_en)" -match "灼烧") {
        $tags = @($tags + "BurnReference" | Select-Object -Unique)
    }
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
        rarity = [Math]::Max(1, [Math]::Min(4, (Convert-ToInt $row.Rarity 1)))
        exhaust = $tags -contains "Burnout" `
            -or $tags -contains "Fragmented" `
            -or "$($row.UseScript) $($row.InitScript)" -match "(?i)exhaust|消耗|RemoveCard"
        tags = $tags
        requiresEnemyTarget = $effects.Where({ $_.target -eq "SelectedEnemy" }).Count -gt 0
        fidelity = "Approximate"
        effects = @($effects)
    }
}

function Try-NewAuthoritativeMoneyCard(
    [object]$row,
    [ref]$definition) {
    $definition.Value = $null
    $id = [string]$row.Id
    $tags = @(([string]$row.Tag -split "\||,|，|;|；") |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    $effects = @()
    switch ($id) {
        "card_5" {
            $effects += [ordered]@{
                kind = "ModifyVariable"
                target = "Self"
                amount = 30
                definitionId = "Money"
                persistAcrossBattles = $true
                minimumVariableValue = 0
            }
        }
        "luckycard_3" {
            foreach ($hit in 1..20) {
                $effects += [ordered]@{
                    kind = "Damage"
                    target = "AllEnemies"
                    amount = 6
                    conditionExpression = New-CombatOperationExpression `
                        "GreaterThanOrEqual" @(
                            (New-CombatValueExpression "SourceVariable" 0.0 "Money"),
                            (New-CombatValueExpression "Constant" (20 * $hit)))
                }
            }
            $count = New-CombatOperationExpression "Minimum" @(
                (New-CombatOperationExpression "Floor" @(
                    (New-CombatOperationExpression "Divide" @(
                        (New-CombatValueExpression "SourceVariable" 0.0 "Money"),
                        (New-CombatValueExpression "Constant" 20))))),
                (New-CombatValueExpression "Constant" 20))
            $effects += [ordered]@{
                kind = "ModifyVariable"
                target = "Self"
                amountExpression = New-CombatOperationExpression "Multiply" @(
                    $count,
                    (New-CombatValueExpression "Constant" -10))
                rounding = "Truncate"
                definitionId = "Money"
                persistAcrossBattles = $true
                minimumVariableValue = 0
            }
        }
        "luckycard_10" {
            $effects += [ordered]@{
                kind = "AddStatus"
                target = "Self"
                definitionId = "buff_resilient"
                amountExpression = New-CombatOperationExpression "Floor" @(
                    (New-CombatOperationExpression "Divide" @(
                        (New-CombatValueExpression "SourceVariable" 0.0 "Money"),
                        (New-CombatValueExpression "Constant" 100))))
                conditionExpression = New-CombatOperationExpression `
                    "GreaterThanOrEqual" @(
                        (New-CombatValueExpression "SourceVariable" 0.0 "Money"),
                        (New-CombatValueExpression "Constant" 100))
                rounding = "Truncate"
            }
        }
        default {
            return $false
        }
    }
    $definition.Value = [ordered]@{
        ownerModId = "Witch"
        cardId = $id
        displayName = [string]$row.Name
        cost = [Math]::Max(0, [Math]::Min(9, (Convert-ToInt $row.Expend 1)))
        rarity = [Math]::Max(1, [Math]::Min(4, (Convert-ToInt $row.Rarity 1)))
        exhaust = $tags -contains "Burnout" -or $tags -contains "Fragmented"
        tags = $tags
        requiresEnemyTarget = $false
        fidelity = "Authoritative"
        effects = @($effects)
    }
    return $true
}

function Try-NewAuthoritativeCurseCard(
    [object]$row,
    [ref]$definition) {
    $definition.Value = $null
    $id = [string]$row.Id
    $tags = @(([string]$row.Tag -split "\||,|，|;|；") |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    $effects = @()
    $drawEffects = @()
    $discardEffects = @()
    switch ($id) {
        "cursecard_1" {
            $drawEffects += [ordered]@{
                kind = "AddStatus"; target = "Self"
                definitionId = "buff_vulnerability"; amount = 5
            }
        }
        "cursecard_2" {
            $drawEffects += [ordered]@{
                kind = "AddStatus"; target = "Self"
                definitionId = "buff_chaos"; amount = 1
            }
        }
        "cursecard_3" {
            $drawEffects += [ordered]@{
                kind = "AddStatus"; target = "Self"
                definitionId = "buff_weak"; amount = 2
            }
        }
        "cursecard_4" {
            $discardEffects += [ordered]@{
                kind = "AddStatus"; target = "Self"
                definitionId = "buff_degrade"; amount = 2
            }
        }
        "cursecard_5" {
            $drawEffects += [ordered]@{
                kind = "AddStatus"; target = "Self"
                definitionId = "buff_rotten"; amount = 1
            }
        }
        "cursecard_8" {
            $drawEffects += [ordered]@{
                kind = "AddStatus"; target = "Self"
                definitionId = "buff_weak"; amount = 1
            }
        }
        "cursecard_9" {
            $drawEffects += [ordered]@{
                kind = "AddStatus"; target = "Self"
                definitionId = "buff_cripple"; amount = 2
            }
        }
        "cursecard_10" {
            $drawEffects += [ordered]@{
                kind = "AddStatus"; target = "Self"
                definitionId = "buff_bleeding"; amount = 1
            }
        }
        "cursecard_11" {
            # Native script is intentionally empty: this curse only occupies
            # a draw and can be burned by playing it.
        }
        "cursecard_12" {
            $discardEffects += [ordered]@{
                kind = "AddStatus"; target = "Self"
                definitionId = "buff_toxin"; amount = 1
            }
        }
        "cursecard_13" {
            $effects += [ordered]@{
                kind = "DirectHpLoss"; target = "Self"; amount = 10
            }
            foreach ($attribute in @("Lucky", "Strength", "Wisdom", "Perceive")) {
                $effects += [ordered]@{
                    kind = "ModifyVariable"; target = "Self"; amount = -2
                    definitionId = $attribute
                }
            }
            $discardEffects += [ordered]@{
                kind = "CreateCard"; target = "Self"; amount = 1
                definitionId = "cursecard_13"
                destinationZone = "DrawPile"
                randomizeDestination = $true
            }
        }
        default {
            return $false
        }
    }
    $definition.Value = [ordered]@{
        ownerModId = "Witch"
        cardId = $id
        displayName = [string]$row.Name
        cost = [Math]::Max(0, [Math]::Min(9, (Convert-ToInt $row.Expend 1)))
        rarity = [Math]::Max(1, [Math]::Min(4, (Convert-ToInt $row.Rarity 1)))
        exhaust = $tags -contains "Burnout" -or $tags -contains "Fragmented"
        tags = $tags
        requiresEnemyTarget = $false
        fidelity = "Authoritative"
        effects = @($effects)
        drawEffects = @($drawEffects)
        discardEffects = @($discardEffects)
    }
    return $true
}

function Try-NewAuthoritativeDeterministicCard(
    [object]$row,
    [ref]$definition) {
    $definition.Value = $null
    $id = [string]$row.Id
    $tags = @(([string]$row.Tag -split "\||,|，|;|；") |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    $effects = @()
    $drawEffects = @()
    $requiresEnemyTarget = $false
    $sourceHp = New-CombatValueExpression "SourceHp"
    $sourceMaxHp = New-CombatValueExpression "SourceMaxHp"
    switch ($id) {
        "universalcard_1" {
            $drawEffects += [ordered]@{
                kind = "GainEnergy"; target = "Self"; amount = 1
            }
            $drawEffects += [ordered]@{
                kind = "Draw"; target = "Self"; amount = 1
            }
        }
        "Crowdfundingcard_45" {
            $effects += [ordered]@{
                kind = "ModifyVariable"; target = "Self"; amount = 5
                definitionId = "Lucky"
            }
        }
        "counterattackcard_1" {
            $missingHp = New-CombatOperationExpression "Subtract" @(
                $sourceMaxHp,
                $sourceHp)
            $extraPoised = New-CombatOperationExpression "Floor" @(
                (New-CombatOperationExpression "Divide" @(
                    $missingHp,
                    (New-CombatValueExpression "Constant" 25))))
            $effects += [ordered]@{
                kind = "AddStatus"; target = "Self"
                definitionId = "buff_poised"
                amountExpression = New-CombatOperationExpression "Add" @(
                    (New-CombatValueExpression "Constant" 1),
                    $extraPoised)
                rounding = "Truncate"
            }
        }
        "Crowdfundingcard_49" {
            $oneFifthMaxHp = New-CombatOperationExpression "Divide" @(
                $sourceMaxHp,
                (New-CombatValueExpression "Constant" 5))
            $lostHp = New-CombatOperationExpression "Floor" @($oneFifthMaxHp)
            $oneFourthLostHp = New-CombatOperationExpression "Divide" @(
                $lostHp,
                (New-CombatValueExpression "Constant" 4))
            $statusStacks = New-CombatOperationExpression "Floor" @($oneFourthLostHp)
            $effects += [ordered]@{
                kind = "DirectHpLoss"; target = "Self"
                amountExpression = $lostHp; rounding = "Truncate"
            }
            foreach ($statusId in @("buff_rebirth", "buff_keenedge")) {
                $effects += [ordered]@{
                    kind = "AddStatus"; target = "Self"
                    definitionId = $statusId
                    amountExpression = $statusStacks
                    rounding = "Truncate"
                }
            }
        }
        "SpellCard_14" {
            $oneTenthMaxHp = New-CombatOperationExpression "Divide" @(
                $sourceMaxHp,
                (New-CombatValueExpression "Constant" 10))
            $roundedHpLoss = New-CombatOperationExpression "Floor" @(
                $oneTenthMaxHp)
            $lostHp = New-CombatOperationExpression "Maximum" @(
                (New-CombatValueExpression "Constant" 1),
                $roundedHpLoss)
            $effects += [ordered]@{
                kind = "DirectHpLoss"; target = "Self"
                amountExpression = $lostHp; rounding = "Truncate"
            }
            $effects += [ordered]@{
                kind = "CreateCard"; target = "Self"; amount = 1
                definitionId = "SpellCard_4"; destinationZone = "Hand"
            }
        }
        "SpellCard_17" {
            $scaledHp = New-CombatOperationExpression "Multiply" @(
                $sourceHp,
                (New-CombatValueExpression "Constant" 0.6))
            $roundedHpLoss = New-CombatOperationExpression "Ceiling" @($scaledHp)
            $lostHp = New-CombatOperationExpression "Maximum" @(
                (New-CombatValueExpression "Constant" 1),
                $roundedHpLoss)
            $effects += [ordered]@{
                kind = "DirectHpLoss"; target = "Self"
                amountExpression = $lostHp; rounding = "Truncate"
            }
            $effects += [ordered]@{
                kind = "Damage"; target = "AllEnemies"
                amountExpression = New-CombatOperationExpression "Multiply" @(
                    $lostHp,
                    (New-CombatValueExpression "Constant" 2))
                rounding = "Truncate"
            }
            $effects += [ordered]@{
                kind = "CreateCard"; target = "Self"; amount = 1
                definitionId = "SpellCard_4"; destinationZone = "Hand"
            }
        }
        "universalcard_10" {
            $effects += [ordered]@{
                kind = "SetHp"; target = "Self"; amount = 10
            }
            $missingAfterSet = New-CombatOperationExpression "Subtract" @(
                $sourceMaxHp,
                (New-CombatValueExpression "Constant" 10))
            $scaledMissingHp = New-CombatOperationExpression "Multiply" @(
                $missingAfterSet,
                (New-CombatValueExpression "Constant" 3))
            $damageRatio = New-CombatOperationExpression "Divide" @(
                $scaledMissingHp,
                (New-CombatValueExpression "Constant" 10))
            $effects += [ordered]@{
                kind = "Damage"; target = "SelectedEnemy"
                amountExpression = New-CombatOperationExpression "Floor" @(
                    $damageRatio)
                rounding = "Truncate"
            }
            $requiresEnemyTarget = $true
        }
        "universalcard_14" {
            $effects += [ordered]@{
                kind = "SetHp"; target = "Self"; amount = 25
            }
            $effects += [ordered]@{
                kind = "GainBlock"; target = "Self"; amount = 15
            }
            $effects += [ordered]@{
                kind = "AddStatus"; target = "Self"
                definitionId = "buff_resilient"; amount = 2
            }
        }
        "universalcard_15" {
            $effects += [ordered]@{
                kind = "Draw"; target = "Self"; amount = 4
            }
            $effects += [ordered]@{
                kind = "SetHp"; target = "Self"; amount = 10
            }
            $effects += [ordered]@{
                kind = "GainEnergy"; target = "Self"; amount = 3
            }
        }
        "Crowdfundingcard_25" {
            $halfMaxHp = New-CombatOperationExpression "Divide" @(
                $sourceMaxHp,
                (New-CombatValueExpression "Constant" 2))
            $roundedHalfMaxHp = New-CombatOperationExpression "Floor" @($halfMaxHp)
            $effects += [ordered]@{
                kind = "SetHp"; target = "Self"
                amountExpression = New-CombatOperationExpression "Maximum" @(
                    (New-CombatValueExpression "Constant" 1),
                    $roundedHalfMaxHp)
                rounding = "Truncate"
            }
            $effects += [ordered]@{
                kind = "AddStatus"; target = "Self"
                definitionId = "buff_synergies"; amount = 1
            }
        }
        default {
            return $false
        }
    }
    $definition.Value = [ordered]@{
        ownerModId = "Witch"
        cardId = $id
        displayName = [string]$row.Name
        cost = [Math]::Max(0, [Math]::Min(9, (Convert-ToInt $row.Expend 1)))
        rarity = [Math]::Max(1, [Math]::Min(4, (Convert-ToInt $row.Rarity 1)))
        exhaust = $tags -contains "Burnout" -or $tags -contains "Fragmented"
        tags = $tags
        requiresEnemyTarget = $requiresEnemyTarget
        fidelity = "Authoritative"
        effects = @($effects)
        drawEffects = @($drawEffects)
        discardEffects = @()
    }
    return $true
}

function Convert-PlayerCardTarget([string]$status) {
    $normalized = if ($null -eq $status) { "" } else { $status.Trim() }
    switch -Regex ($normalized) {
        "^(Self|AllFriends|AllFriendExSelf)$" { return "Self" }
        "^AllTarget$" { return "AllEnemies" }
        "^(Target|AllRandomTarget1)$" { return "SelectedEnemy" }
        default { return "" }
    }
}

function Try-NewDirectAuthoritativeCard(
    [object]$row,
    [ref]$definition) {
    $definition.Value = $null
    $use = [string]$row.UseScript
    if ([string]::IsNullOrWhiteSpace($use) `
        -or -not [string]::IsNullOrWhiteSpace([string]$row.DrawScript) `
        -or -not [string]::IsNullOrWhiteSpace([string]$row.DropScript) `
        -or $use -match '(?i)\b(if|for|foreach|while|switch|return)\b|=>|\b(Math|PlayerInfo|Vars|HandCard|DeckCard|UsedCard|Object)\b|(?:Self|Target)\.') {
        return $false
    }

    $tags = @(([string]$row.Tag -split "\||,|，|;|；") |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    if ("$($row.Description) $($row.Description_en)" -match "灼烧") {
        $tags = @($tags + "BurnReference" | Select-Object -Unique)
    }
    $effects = @()
    $target = "Self"
    foreach ($rawPart in ($use -split ';')) {
        $part = $rawPart.Trim()
        if ([string]::IsNullOrWhiteSpace($part)) {
            continue
        }
        $match = [regex]::Match(
            $part,
            '^SetStatus\(\s*"([^"]+)"\s*\)$',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            $target = Convert-PlayerCardTarget $match.Groups[1].Value
            if ([string]::IsNullOrWhiteSpace($target)) {
                return $false
            }
            continue
        }
        $match = [regex]::Match(
            $part,
            '^Damage\(\s*"?(\d+)"?\s*(?:,\s*"?(True|Normal)"?)?\s*\)$',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            $effects += [ordered]@{
                kind = if ($match.Groups[2].Value -eq "True") {
                    "TrueDamage"
                } else {
                    "Damage"
                }
                target = $target
                amount = Convert-ToInt $match.Groups[1].Value 0
            }
            continue
        }
        $match = [regex]::Match(
            $part,
            '^ChangeDefence\(\s*"?(\d+)"?\s*\)$',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            $effects += [ordered]@{
                kind = "GainBlock"
                target = $target
                amount = Convert-ToInt $match.Groups[1].Value 0
            }
            continue
        }
        $match = [regex]::Match(
            $part,
            '^DrawCount\(\s*"?(\d+)"?\s*\)$',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            $effects += [ordered]@{
                kind = "Draw"
                target = "Self"
                amount = Convert-ToInt $match.Groups[1].Value 0
            }
            continue
        }
        $match = [regex]::Match(
            $part,
            '^ChangePower\(\s*"?(\d+)"?\s*\)$',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            $effects += [ordered]@{
                kind = "GainEnergy"
                target = "Self"
                amount = Convert-ToInt $match.Groups[1].Value 0
            }
            continue
        }
        $match = [regex]::Match(
            $part,
            '^ChangeHp\(\s*"?(-?\d+)"?\s*\)$',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            $value = Convert-ToInt $match.Groups[1].Value 0
            $effects += [ordered]@{
                kind = if ($value -lt 0) { "DirectHpLoss" } else { "Heal" }
                target = "Self"
                amount = [Math]::Abs($value)
            }
            continue
        }
        $match = [regex]::Match(
            $part,
            '^AddBuff\(\s*(?:DataId\.)?"?([A-Za-z0-9_]+)"?\s*,\s*"?(\d+)"?\s*\)$',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            $effects += [ordered]@{
                kind = "AddStatus"
                target = $target
                definitionId = $match.Groups[1].Value
                amount = Convert-ToInt $match.Groups[2].Value 0
            }
            continue
        }
        $match = [regex]::Match(
            $part,
            '^RemoveBuff\(\s*(?:DataId\.)?"?([A-Za-z0-9_]+)"?\s*\)$',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            $effects += [ordered]@{
                kind = "RemoveStatus"
                target = $target
                definitionId = $match.Groups[1].Value
                amount = 1
            }
            continue
        }
        $match = [regex]::Match(
            $part,
            '^CreateCard\(\s*new\s+DataConfig\(\s*DataId\.([A-Za-z0-9_]+)\s*,\s*DataType\.Card\s*\)\s*\)$',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            $effects += [ordered]@{
                kind = "CreateCard"
                target = "Self"
                definitionId = $match.Groups[1].Value
                amount = 1
                destinationZone = "Hand"
            }
            continue
        }
        return $false
    }
    if ($effects.Count -eq 0) {
        return $false
    }

    $definition.Value = [ordered]@{
        ownerModId = "Witch"
        cardId = [string]$row.Id
        displayName = [string]$row.Name
        cost = [Math]::Max(0, [Math]::Min(9, (Convert-ToInt $row.Expend 1)))
        rarity = [Math]::Max(1, [Math]::Min(4, (Convert-ToInt $row.Rarity 1)))
        exhaust = $tags -contains "Burnout" -or $tags -contains "Fragmented"
        tags = $tags
        requiresEnemyTarget = $effects.Where({
            $_.target -eq "SelectedEnemy"
        }).Count -gt 0
        fidelity = "Authoritative"
        effects = @($effects)
    }
    return $true
}

function Try-NewTaggedRetrievalCard(
    [object]$row,
    [ref]$definition) {
    $definition.Value = $null
    $id = [string]$row.Id
    $amount = 0
    $requiredCardTag = ""
    $sourceZone = "DrawPile"
    switch ($id) {
        "ritualcard_1" {
            $amount = 2
            $requiredCardTag = "Ritual"
        }
        "timekeeper_15" {
            $amount = 3
            $requiredCardTag = "Instant"
        }
        "timekeeper_16" {
            $amount = 3
            $requiredCardTag = "Froze"
            $sourceZone = "DiscardPile"
        }
        "timekeeper_18" {
            $amount = 2
        }
        default {
            return $false
        }
    }

    $tags = @(([string]$row.Tag -split "\||,|，|;|；") |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    $definition.Value = [ordered]@{
        ownerModId = "Witch"
        cardId = $id
        displayName = [string]$row.Name
        cost = [Math]::Max(0, [Math]::Min(9, (Convert-ToInt $row.Expend 1)))
        rarity = [Math]::Max(1, [Math]::Min(4, (Convert-ToInt $row.Rarity 1)))
        exhaust = $tags -contains "Burnout" -or $tags -contains "Fragmented"
        tags = $tags
        requiresEnemyTarget = $false
        fidelity = "Authoritative"
        effects = @(
            [ordered]@{
                kind = "RetrieveCards"
                target = "Self"
                amount = $amount
                requiredCardTag = $requiredCardTag
                sourceZone = $sourceZone
                destinationZone = "Hand"
            })
    }
    return $true
}

function Get-ScriptInteger(
    [string]$script,
    [string]$pattern,
    [int]$fallback) {
    $source = if ($null -eq $script) { "" } else { $script }
    $match = [regex]::Match(
        $source,
        $pattern,
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($match.Success) {
        return Convert-ToInt $match.Groups[1].Value $fallback
    }
    return $fallback
}

function New-CombatValueExpression(
    [string]$operation,
    [double]$constant = 0.0,
    [string]$key = "",
    [object[]]$arguments = @()) {
    $result = [ordered]@{ operation = $operation }
    if ($operation -eq "Constant") {
        $result.constant = $constant
    }
    if (-not [string]::IsNullOrWhiteSpace($key)) {
        $result.key = $key
    }
    if ($arguments.Count -gt 0) {
        $result.arguments = @($arguments)
    }
    return $result
}

function New-CombatOperationExpression(
    [string]$operation,
    [object[]]$arguments) {
    return New-CombatValueExpression $operation 0.0 "" $arguments
}

function New-CombatConditionalExpression(
    [object]$condition,
    [object]$whenTrue,
    [object]$whenFalse) {
    return New-CombatOperationExpression "Conditional" @(
        $condition,
        $whenTrue,
        $whenFalse)
}

function New-EnemyEffect(
    [string]$kind,
    [string]$target,
    [int]$amount = 0,
    [string]$definitionId = "",
    [object]$amountExpression = $null) {
    $effect = [ordered]@{
        kind = $kind
        target = $target
        amount = $amount
    }
    if (-not [string]::IsNullOrWhiteSpace($definitionId)) {
        $effect.definitionId = $definitionId
    }
    if ($null -ne $amountExpression) {
        $effect.amountExpression = $amountExpression
        $effect.rounding = "Truncate"
    }
    return $effect
}

function New-EnemyCreateCardEffect([string]$cardId) {
    return [ordered]@{
        kind = "CreateCard"
        target = "Player"
        definitionId = $cardId
        amount = 1
        destinationZone = "DrawPile"
        randomizeDestination = $true
    }
}

function Try-NewAuthoritativeEnemyIntent(
    [object]$card,
    [object]$enemy,
    [ref]$definition) {
    $definition.Value = $null
    $id = [string]$card.Id
    $attack = [Math]::Max(1, (Convert-ToInt $enemy.Attack 5))
    $block = [Math]::Max(0, (Convert-ToInt $enemy.Defend 0))
    $priority = Get-ScriptInteger ([string]$card.InitScript) 'Vars\["priority"\]\s*=\s*"(\d+)"' 1
    $cooldown = Get-ScriptInteger ([string]$card.InitScript) 'Vars\["CD"\]\s*=\s*"(\d+)"' 0
    $effects = @()
    $priorityExpression = $null
    $cooldownExpression = $null
    $availabilityExpression = $null

    $constant = { param([double]$value) New-CombatValueExpression "Constant" $value }
    $sourceVariable = {
        param([string]$key)
        New-CombatValueExpression "SourceVariable" 0.0 $key
    }
    $sourceHpRatio = New-CombatOperationExpression "Divide" @(
        (New-CombatValueExpression "SourceHp"),
        (New-CombatValueExpression "SourceMaxHp"))

    if ($id -in @("enemycard_Charge1", "enemycard_Charge2")) {
        if ($id -eq "enemycard_Charge1") {
            $cooldownExpression = New-CombatConditionalExpression (
                New-CombatOperationExpression "GreaterThan" @(
                    (& $sourceVariable "TagDiff"),
                    (& $constant 20))) (
                & $constant 3) (
                & $constant 2)
        }
    } elseif ($id -eq "enemycard_thief") {
        $effect = New-EnemyEffect "ModifyVariable" "Player" -10 "Money"
        $effect.persistAcrossBattles = $true
        $effect.minimumVariableValue = 0
        $effects += $effect
    } elseif ($id -eq "enemycard_Thieves") {
        $effect = New-EnemyEffect "ModifyVariable" "Player" -15 "Money"
        $effect.persistAcrossBattles = $true
        $effect.minimumVariableValue = 0
        $effects += $effect
    } elseif ($id -eq "enemycard_obtainMoney") {
        $effects += New-EnemyEffect "Damage" "Player" $attack
        $steal = New-EnemyEffect "ModifyVariable" "Player" -15 "Money"
        $steal.persistAcrossBattles = $true
        $steal.minimumVariableValue = 0
        $effects += $steal
        $refund = New-EnemyEffect "DeferVariableUntilVictory" "Player" 15 "Money"
        $refund.persistAcrossBattles = $true
        $refund.minimumVariableValue = 0
        $effects += $refund
        $priorityExpression = New-CombatConditionalExpression (
            New-CombatOperationExpression "GreaterThan" @(
                (& $sourceVariable "TagDiff"),
                (& $constant 30))) (
            & $constant 4) (
            & $constant 0)
    } elseif ($id -eq "enemycard_OrdinaryHit") {
        $effects += New-EnemyEffect "Damage" "Player" $attack
    } elseif ($id -eq "enemycard_QuadrupleHits") {
        1..4 | ForEach-Object {
            $effects += New-EnemyEffect "Damage" "Player" (
                [Math]::Max(1, [Math]::Floor($attack / 3)))
        }
    } elseif ($id -eq "enemycard_OrdinaryFiveHit") {
        1..5 | ForEach-Object {
            $effects += New-EnemyEffect "Damage" "Player" (
                [Math]::Max(1, [Math]::Floor($attack * 3 / 10)))
        }
    } elseif ($id -eq "enemycard_FiveHit") {
        1..5 | ForEach-Object {
            $effects += New-EnemyEffect "Damage" "Player" (
                [Math]::Max(1, [Math]::Floor($attack / 5)))
        }
    } elseif ($id -eq "enemycard_foraging") {
        $effects += New-EnemyEffect "Damage" "Player" (
            [Math]::Max(1, [Math]::Floor($attack * 4 / 5)))
    } elseif ($id -eq "enemycard_SuperFireBall") {
        1..2 | ForEach-Object {
            $effects += New-EnemyEffect "Damage" "Player" $attack
        }
    } elseif ($id -eq "enemycard_GiantClawStrike") {
        $effects += New-EnemyEffect "Damage" "Player" ($attack * 2)
        $effects += New-EnemyEffect "AddStatus" "Player" 2 "buff_bleeding"
    } elseif ($id -eq "enemycard_SpreadWings") {
        $effects += New-EnemyEffect "Damage" "Player" $attack
        $effects += New-EnemyEffect "AddStatus" "Player" 1 "buff_burn"
    } elseif ($id -in @("enemycard_Seduce", "enemycard_CAR_Spear")) {
        $effects += New-EnemyEffect "Damage" "Player" $attack
        $effects += New-EnemyEffect "AddStatus" "Player" 2 "buff_vulnerability"
    } elseif ($id -eq "enemycard_psychologicalShock") {
        $effects += New-EnemyEffect "Damage" "Player" (
            [Math]::Max(1, [Math]::Floor($attack * 2 / 3)))
        $effects += New-EnemyCreateCardEffect "cursecard_6"
        $priorityExpression = New-CombatConditionalExpression (
            New-CombatOperationExpression "GreaterThan" @(
                (& $sourceVariable "TagDiff"),
                (& $constant 30))) (
            & $constant 4) (
            & $constant 0)
    } elseif ($id -eq "enemycard_VenomSpray") {
        $effects += New-EnemyEffect "Damage" "Player" (
            [Math]::Max(1, [Math]::Floor($attack * 2 / 3)))
        $effects += New-EnemyCreateCardEffect "cursecard_12"
        $priorityExpression = New-CombatConditionalExpression (
            New-CombatOperationExpression "GreaterThan" @(
                (& $sourceVariable "TagDiff"),
                (& $constant 20))) (
            & $constant 4) (
            & $constant 0)
    } elseif ($id -eq "enemycard_specialAttack") {
        $effects += New-EnemyEffect "GainBlock" "Self" $block
        $effects += New-EnemyEffect "Damage" "Player" $attack
    } elseif ($id -eq "enemycard_Despair") {
        $effects += New-EnemyEffect "Damage" "Player" (
            [Math]::Max(1, [Math]::Floor($attack * 4 / 5)))
        $effects += New-EnemyEffect "AddStatus" "Player" 3 "buff_toxin"
        $midHealth = New-CombatOperationExpression "Multiply" @(
            (New-CombatOperationExpression "LessThanOrEqual" @(
                $sourceHpRatio,
                (& $constant 0.65))),
            (New-CombatOperationExpression "GreaterThan" @(
                $sourceHpRatio,
                (& $constant 0.3))))
        $priorityExpression = New-CombatConditionalExpression $midHealth (
            & $constant 5) (
            & $constant 0)
    } elseif ($id -eq "enemycard_CAR_Sword") {
        $effects += New-EnemyEffect "Damage" "Player" (
            [Math]::Max(1, [Math]::Floor($attack * 3 / 2)))
        $effects += New-EnemyEffect "TrueDamage" "Player" 0 "" (
            New-CombatOperationExpression "Divide" @(
                (New-CombatValueExpression "TargetMaxHp"),
                (& $constant 5)))
    } elseif ($id -eq "enemycard_CAR_Hammer") {
        $effects += New-EnemyEffect "Damage" "Player" $attack
        $oneThirdBlock = New-CombatOperationExpression "Divide" @(
            (New-CombatValueExpression "TargetBlock"),
            (& $constant 3))
        $flooredThirdBlock = New-CombatOperationExpression "Floor" @(
            $oneThirdBlock)
        $blockReduction = New-CombatOperationExpression "Maximum" @(
            (& $constant 1),
            $flooredThirdBlock)
        $remainingBlock = New-CombatOperationExpression "Subtract" @(
            (New-CombatValueExpression "TargetBlock"),
            $blockReduction)
        $setBlock = New-EnemyEffect "SetBlock" "Player" 0 "" $remainingBlock
        $setBlock.conditionExpression = New-CombatOperationExpression "GreaterThan" @(
            (New-CombatValueExpression "TargetBlock"),
            (& $constant 0))
        $effects += $setBlock
        $effects += New-EnemyEffect `
            "ModifyVariablePercent" `
            "Player" `
            -20 `
            "HealMultiplier"
    } elseif ($id -eq "enemycard_defence") {
        $effects += New-EnemyEffect "GainBlock" "Self" $block
    } elseif ($id -eq "enemycard_FullSupport") {
        $effects += New-EnemyEffect "GainBlock" "AllEnemies" (
            [Math]::Max(1, [Math]::Floor($block * 2 / 3)))
    } elseif ($id -eq "enemycard_RoyalBarrier") {
        $effects += New-EnemyEffect "GainBlock" "Self" ($block * 2)
    } elseif ($id -eq "enemycard_MakeIneffectiveRays1") {
        $effects += New-EnemyEffect "GainBlock" "Self" $block
        $effects += New-EnemyEffect "AddStatus" "Self" 2 "buff_impregnable"
    } elseif ($id -eq "enemycard_HighFly") {
        $effects += New-EnemyEffect "GainBlock" "Self" (2 * $block)
        $effects += New-EnemyEffect "GainBlock" "Self" 0 "" (
            New-CombatValueExpression "SourceStatusStacks" 0.0 "buff_burn")
    } elseif ($id -eq "enemycard_rejuvenation") {
        $effects += New-EnemyEffect "Heal" "Self" (
            [Math]::Max(1, [Math]::Floor($attack * 4 / 5)))
    } elseif ($id -eq "enemycard_CAR_Shield") {
        $value = [Math]::Max(1, [Math]::Floor($block / 5))
        $effects += New-EnemyEffect "AddStatus" "Self" $value "buff_impregnable"
        $effects += New-EnemyEffect "AddStatus" "Self" $value "buff_vitality"
    } elseif ($id -eq "enemycard_burn") {
        $amount = New-CombatConditionalExpression (
            New-CombatOperationExpression "GreaterThan" @(
                (& $sourceVariable "TagDiff"),
                (& $constant 20))) (
            & $constant 4) (
            & $constant 3)
        $effects += New-EnemyEffect "AddStatus" "Player" 0 "buff_burn" $amount
    } elseif ($id -eq "enemycard_Toxin2") {
        $amount = New-CombatConditionalExpression (
            New-CombatOperationExpression "GreaterThan" @(
                (& $sourceVariable "TagDiff"),
                (& $constant 16))) (
            & $constant 4) (
            & $constant 3)
        $effects += New-EnemyEffect "AddStatus" "Player" 0 "buff_toxin" $amount
    } elseif ($id -eq "enemycard_Toxin1") {
        $effects += New-EnemyEffect "AddStatus" "Player" 3 "buff_toxin"
        $cooldownExpression = New-CombatConditionalExpression (
            New-CombatOperationExpression "GreaterThan" @(
                (& $sourceVariable "Difficulty"),
                (& $constant 3))) (
            & $constant 0) (
            & $constant 1)
    } elseif ($id -in @("enemycard_Toxin3", "enemycard_Toxin4")) {
        $effects += New-EnemyEffect "AddStatus" "Player" 5 "buff_toxin"
    } elseif ($id -eq "enemycard_burn1") {
        $effects += New-EnemyEffect "AddStatus" "Player" 3 "buff_burn"
    } elseif ($id -eq "enemycard_burn2") {
        $effects += New-EnemyEffect "AddStatus" "Player" 6 "buff_burn"
    } elseif ($id -eq "enemycard_charmed") {
        $effects += New-EnemyEffect "AddStatus" "Player" 1 "buff_timestop"
    } elseif ($id -eq "enemycard_FallenDragon") {
        $effects += New-EnemyEffect "AddStatus" "AllEnemies" 20 "buff_extraordinary"
    } elseif ($id -eq "enemycard_fearless") {
        $effects += New-EnemyEffect "AddStatus" "Self" 4 "buff_vitality"
    } elseif ($id -eq "enemycard_IceShield") {
        $effects += New-EnemyEffect "AddStatus" "Player" 2 "buff_cripple"
    } elseif ($id -eq "enemycard_Licking") {
        $effects += New-EnemyEffect "AddStatus" "Player" 2 "buff_rotten"
    } elseif ($id -eq "enemycard_LimePowder") {
        $effects += New-EnemyEffect "AddStatus" "Player" 1 "buff_oblivion"
    } elseif ($id -eq "enemycard_NerveReflexes") {
        $effects += New-EnemyEffect "AddStatus" "Self" 1 "buff_frenzy"
    } elseif ($id -eq "enemycard_NeverDead") {
        $effects += New-EnemyEffect "AddStatus" "Self" 5 "buff_evergreen"
    } elseif ($id -eq "enemycard_Observe") {
        $effects += New-EnemyEffect "AddStatus" "Player" 2 "buff_degrade"
    } elseif ($id -eq "enemycard_OverrunWorkouts") {
        $effects += New-EnemyEffect "AddStatus" "Self" (
            [Math]::Max(1, [Math]::Floor($attack / 5))) "buff_keenedge"
    } elseif ($id -eq "enemycard_PoisonThrowing") {
        $effects += New-EnemyEffect "AddStatus" "Player" 1 "buff_oblivion"
        $effects += New-EnemyEffect "AddStatus" "Player" 3 "buff_toxin"
    } elseif ($id -eq "enemycard_vulnerabilityLight") {
        $effects += New-EnemyEffect "AddStatus" "Player" 3 "buff_vulnerability"
    } elseif ($id -eq "enemycard_Witness") {
        $effects += New-EnemyEffect "AddStatus" "Self" 2 "buff_impregnable"
    } elseif ($id -eq "enemycard_Weak") {
        $effects += New-EnemyEffect "AddStatus" "Self" (
            [Math]::Max(1, [Math]::Floor($attack / 5))) "buff_weak"
    } elseif ($id -eq "enemycard_WeakLight") {
        $effects += New-EnemyEffect "AddStatus" "Player" 2 "buff_weak"
    } elseif ($id -eq "enemycard_MT1") {
        $effects += New-EnemyEffect "AddStatus" "AllEnemies" 2 "buff_lifelink"
        $priorityExpression = New-CombatConditionalExpression (
            New-CombatOperationExpression "LessThan" @(
                $sourceHpRatio,
                (& $constant 0.8))) (
            & $constant 9) (
            & $constant 10)
    } elseif ($id -eq "enemycard_MT2") {
        $counterKey = "IntentUse:$id"
        $availabilityExpression = New-CombatOperationExpression "LessThan" @(
            (& $sourceVariable $counterKey),
            (& $constant 2))
        $effects += New-EnemyEffect `
            "AddStatus" `
            "AllAlliesExceptSelf" `
            10 `
            "buff_extraordinary"
        $effects += New-EnemyEffect `
            "ModifyVariable" `
            "AllAlliesExceptSelf" `
            1 `
            "ActionCountBonus"
        $effects += New-EnemyEffect "ModifyVariable" "Self" 1 $counterKey
    } elseif ($id -eq "enemycard_Wake") {
        $effect = New-EnemyEffect `
            "ModifyStatusCounter" `
            "AllAlliesExceptSelf" `
            1 `
            "SpecialBuff_GiantDollBear"
        $effect.counterKey = "ThisCount"
        $effects += $effect
    } elseif ($id -eq "enemycard_WhereverYouGo") {
        $effects += New-EnemyEffect "CopyStatuses" "Player" 1
    } elseif ($id -eq "enemycard_OriginalSinCard") {
        1..2 | ForEach-Object {
            $effects += New-EnemyCreateCardEffect "cursecard_13"
        }
    } elseif ($id -in @("enemycard_PlugCards1", "enemycard_PlugCards3")) {
        1..3 | ForEach-Object {
            $effects += New-EnemyCreateCardEffect "card_2"
        }
    } elseif ($id -eq "enemycard_PlugCards2") {
        $effects += New-EnemyCreateCardEffect "cursecard_2"
        $effects += New-EnemyCreateCardEffect "card_1"
    } elseif ($id -eq "enemycard_PowerlessCurse") {
        $effects += New-EnemyCreateCardEffect "cursecard_3"
    } elseif ($id -eq "enemycard_EvilCurse") {
        foreach ($curseIndex in 1..10) {
            $effect = New-EnemyCreateCardEffect "cursecard_$curseIndex"
            $effect.randomChoiceGroup = "random-curse"
            $effect.randomChoiceWeight = 1
            $effects += $effect
        }
    } elseif ($id -eq "enemycard_Dragon'sMajesty") {
        $counterKey = "IntentUse:$id"
        $availabilityExpression = New-CombatOperationExpression "LessThan" @(
            (& $sourceVariable $counterKey),
            (& $constant 2))
        1..2 | ForEach-Object {
            $effects += New-EnemyCreateCardEffect "cursecard_8"
        }
        $third = New-EnemyCreateCardEffect "cursecard_8"
        $third.conditionExpression = New-CombatOperationExpression "GreaterThan" @(
            (& $sourceVariable "Difficulty"),
            (& $constant 3))
        $effects += $third
        $effects += New-EnemyEffect "ModifyVariable" "Self" 1 $counterKey
    } elseif ($id -eq "enemycard_Come") {
        $counterKey = "IntentUse:$id"
        $priorityExpression = New-CombatConditionalExpression (
            New-CombatOperationExpression "GreaterThan" @(
                (New-CombatValueExpression "LivingEnemyCount"),
                (& $constant 2))) (
            & $constant 0) (
            & $constant 4)
        $summonCondition = New-CombatOperationExpression "LessThan" @(
            (& $sourceVariable $counterKey),
            (& $constant 2))
        $summon = New-EnemyEffect "SummonEnemy" "Self" 1 "enemy_10023"
        $summon.conditionExpression = $summonCondition
        $effects += $summon
        $selfDamage = New-EnemyEffect "DirectHpLoss" "Self" 0 "" (
            New-CombatOperationExpression "Multiply" @(
                (New-CombatValueExpression "SourceMaxHp"),
                (& $constant 0.15)))
        $selfDamage.conditionExpression = New-CombatOperationExpression "GreaterThanOrEqual" @(
            (& $sourceVariable $counterKey),
            (& $constant 2))
        $effects += $selfDamage
        $effects += New-EnemyEffect "ModifyVariable" "Self" 1 $counterKey
    } else {
        return $false
    }

    $intent = [ordered]@{
        intentId = $id
        displayName = [string]$card.Name
        weight = 1
        priority = [Math]::Max(0, $priority)
        cooldownTurns = [Math]::Max(0, $cooldown)
        tags = @(([string]$card.Action).Trim() | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_)
        })
        effects = @($effects)
    }
    if ($null -ne $priorityExpression) {
        $intent.priorityExpression = $priorityExpression
    }
    if ($null -ne $cooldownExpression) {
        $intent.cooldownExpression = $cooldownExpression
    }
    if ($null -ne $availabilityExpression) {
        $intent.availabilityExpression = $availabilityExpression
    }
    $definition.Value = $intent
    return $true
}

function Get-ScaledEnemyAmount([string]$script, [int]$attack) {
    if ($script -match "atk\(\).*\*\s*2\s*/\s*3|int\.Parse\(atk\(\)\).*\*\s*2\s*/\s*3") {
        return [Math]::Max(1, [Math]::Floor($attack * 2 / 3))
    }
    if ($script -match "atk\(\).*\*\s*4\s*/\s*5|int\.Parse\(atk\(\)\).*\*\s*4\s*/\s*5") {
        return [Math]::Max(1, [Math]::Floor($attack * 4 / 5))
    }
    if ($script -match "atk\(\).*\*\s*2|int\.Parse\(atk\(\)\).*\*\s*2") {
        return [Math]::Max(1, $attack * 2)
    }
    if ($script -match "atk\(\).*/\s*3" -and $script -match "i\s*<\s*4") {
        return [Math]::Max(1, [Math]::Floor($attack / 3) * 4)
    }
    return [Math]::Max(1, $attack)
}

function Get-EnemyDamageAmounts([string]$script, [int]$attack) {
    $amounts = @()
    if ($script -match "i\s*<\s*5" -and $script -match "atk\(\).*/\s*5") {
        $perHit = [Math]::Max(1, [Math]::Floor($attack / 5))
        return @($perHit, $perHit, $perHit, $perHit, $perHit)
    }
    if ($script -match "i\s*<\s*5" -and $script -match '(?:atk\(\)|Vars\["atk"\]).*\*\s*3\s*/\s*10') {
        $perHit = [Math]::Max(1, [Math]::Floor($attack * 3 / 10))
        return @($perHit, $perHit, $perHit, $perHit, $perHit)
    }
    if ($script -match "i\s*<\s*4" -and $script -match "atk\(\).*/\s*3") {
        $perHit = [Math]::Max(1, [Math]::Floor($attack / 3))
        return @($perHit, $perHit, $perHit, $perHit)
    }
    if ($script -match "i\s*<\s*2" -and $script -match 'Damage\(\s*atk\(\)\s*\)') {
        return @($attack, $attack)
    }
    return @((Get-ScaledEnemyAmount $script $attack))
}

function New-ApproximateEnemyIntent(
    [object]$card,
    [object]$enemy) {
    $init = [string]$card.InitScript
    $use = [string]$card.UseScript
    $targetScript = [string]$card.TargetScript
    $attack = [Math]::Max(1, (Convert-ToInt $enemy.Attack 5))
    $block = [Math]::Max(1, (Convert-ToInt $enemy.Defend 1))
    $priority = Get-ScriptInteger $init 'Vars\["priority"\]\s*=\s*"(\d+)"' 1
    $cooldown = Get-ScriptInteger $init 'Vars\["CD"\]\s*=\s*"(\d+)"' 0
    $effects = @()

    if ($use -match "\bDamage\s*\(") {
        foreach ($damageAmount in @(Get-EnemyDamageAmounts $use $attack)) {
            $effects += [ordered]@{
                kind = "Damage"
                target = "Player"
                amount = $damageAmount
            }
        }
    }
    if ($use -match "\bChangeDefence\s*\(") {
        $effects += [ordered]@{
            kind = "GainBlock"
            target = "Self"
            amount = $block
        }
    }
    if ($use -match "\bChangeHp\s*\(") {
        $effects += [ordered]@{
            kind = "Heal"
            target = "Self"
            amount = Get-ScaledEnemyAmount $use $attack
        }
    }

    $statusTarget = if ($targetScript -match "AllFriends") {
        "AllEnemies"
    } elseif ($targetScript -match "Self") {
        "Self"
    } else {
        "Player"
    }
    foreach ($match in [regex]::Matches(
        $use,
        'AddBuff\(\s*(?:DataId\.)?("?[\w]+"?)\s*,\s*"?(\d+)',
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        $statusId = $match.Groups[1].Value.Trim('"')
        if ([string]::IsNullOrWhiteSpace($statusId)) {
            continue
        }
        $effects += [ordered]@{
            kind = "AddStatus"
            target = $statusTarget
            definitionId = $statusId
            amount = [Math]::Max(1, (Convert-ToInt $match.Groups[2].Value 1))
        }
    }

    $cardMatches = [regex]::Matches(
        $use,
        '(?:RandomAddCard|AddCardToDeckById)\(\s*(?:DataId\.)?([\w]+)\s*\)',
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    foreach ($match in $cardMatches) {
        $repeat = if ($use -match "i\s*<\s*3") {
            3
        } elseif ($use -match "i\s*<\s*2") {
            2
        } else {
            1
        }
        for ($index = 0; $index -lt $repeat; $index++) {
            $effects += [ordered]@{
                kind = "CreateCard"
                target = "Player"
                definitionId = $match.Groups[1].Value
                amount = 1
                destinationZone = "DrawPile"
                randomizeDestination = $true
            }
        }
    }
    if ($use -match 'RandomAddCard\("cursecard_"\+a\)') {
        $effects += [ordered]@{
            kind = "CreateCard"
            target = "Player"
            definitionId = "cursecard_1"
            amount = 1
            destinationZone = "DrawPile"
            randomizeDestination = $true
        }
    }
    if ($use -match "Discard|DropCard") {
        $effects += [ordered]@{
            kind = "DiscardRandom"
            target = "Player"
            amount = 1
        }
    }
    return [ordered]@{
        intentId = [string]$card.Id
        displayName = [string]$card.Name
        weight = 1
        priority = [Math]::Max(0, $priority)
        cooldownTurns = [Math]::Max(0, $cooldown)
        tags = @(([string]$card.Action).Trim() | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_)
        })
        fidelity = "Approximate"
        effects = @($effects)
    }
}

function New-ApproximateEnemy(
    [object]$row,
    [Collections.IDictionary]$enemyCardById) {
    $attack = [Math]::Max(1, (Convert-ToInt $row.Attack 5))
    $intents = @()
    foreach ($cardId in @(([string]$row.CardList -split ",") |
                 ForEach-Object { $_.Trim() } |
                 Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        if ($enemyCardById.Contains($cardId)) {
            $intents += New-ApproximateEnemyIntent $enemyCardById[$cardId] $row
        }
    }
    if ($intents.Count -eq 0) {
        $intents += [ordered]@{
            intentId = "table-basic-attack"
            displayName = "本体表基础攻击（近似）"
            weight = 1
            priority = 1
            effects = @([ordered]@{ kind = "Damage"; target = "Player"; amount = $attack })
        }
    }
    $initialStatuses = @()
    $null = Try-GetAuthoritativeEnemyInitialStatuses $row ([ref]$initialStatuses)
    return [ordered]@{
        ownerModId = "Witch"
        enemyId = [string]$row.Id
        displayName = [string]$row.Name
        maxHp = [Math]::Max(1, (Convert-ToInt $row.Hp 1))
        actionCount = [Math]::Max(1, [Math]::Min(16, (Convert-ToInt $row.ActionCount 1)))
        fidelity = "Approximate"
        variables = [ordered]@{
            BaseAttack = [Math]::Max(0, (Convert-ToInt $row.Attack 0))
            BaseDefend = [Math]::Max(0, (Convert-ToInt $row.Defend 0))
        }
        initialStatuses = @($initialStatuses)
        intents = @($intents)
    }
}

function Try-GetAuthoritativeEnemyInitialStatuses(
    [object]$row,
    [ref]$result) {
    $result.Value = @()
    $enemyId = [string]$row.Id
    $script = [string]$row.InitScript
    if ([string]::IsNullOrWhiteSpace($script)) {
        return $true
    }
    $constant = { param([double]$value) New-CombatValueExpression "Constant" $value }
    $sourceVariable = {
        param([string]$key)
        New-CombatValueExpression "SourceVariable" 0.0 $key
    }
    if ($enemyId -eq "enemy_10005") {
        $highDifficulty = New-CombatOperationExpression "GreaterThan" @(
            (& $sourceVariable "TagDiff"),
            (& $constant 20))
        $result.Value = @(
            [ordered]@{
                statusId = "buff_bloodwall"
                stacks = 4
                conditionExpression = $highDifficulty
            },
            [ordered]@{
                statusId = "buff_bloodwall"
                stacks = 3
                conditionExpression = New-CombatOperationExpression "LessThanOrEqual" @(
                    (& $sourceVariable "TagDiff"),
                    (& $constant 20))
            })
        return $true
    }
    if ($enemyId -eq "enemy_10015") {
        $result.Value = @(
            [ordered]@{
                statusId = "buff_bleeding"
                stacks = [Math]::Max(1, (Convert-ToInt $row.Attack 1))
                stacksExpression = New-CombatOperationExpression "Multiply" @(
                    (& $constant ([Math]::Max(1, (Convert-ToInt $row.Attack 1)))),
                    (& $sourceVariable "AttackScale"))
            })
        return $true
    }
    if ($enemyId -eq "enemy_10027") {
        $result.Value = @(
            [ordered]@{
                statusId = "SpecialBuff_ImmortalGodhead"
                stacks = 3
            },
            [ordered]@{
                statusId = "SpecialBuff_Law:Judgment"
                stacks = 1
                conditionExpression = New-CombatOperationExpression "GreaterThan" @(
                    (& $sourceVariable "TagDiff"),
                    (& $constant 35))
            },
            [ordered]@{
                statusId = "SpecialBuff_Law:Supreme"
                stacks = 1
            },
            [ordered]@{
                statusId = "SpecialBuff_Transcendent"
                stacks = 1
            })
        return $true
    }
    if ($enemyId -eq "enemy_10055") {
        $result.Value = @(
            [ordered]@{
                statusId = "SpecialBuff_HolyJudgementEngine"
                stacks = 1
            })
        return $true
    }

    $matches = [regex]::Matches(
        $script,
        'AddBuff\(\s*"([^"]+)"\s*,\s*"?(\d+)"?\s*\)\s*;?',
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($matches.Count -eq 0) {
        return $false
    }
    $statuses = @()
    foreach ($match in $matches) {
        $statuses += [ordered]@{
            statusId = $match.Groups[1].Value
            stacks = [Math]::Max(1, (Convert-ToInt $match.Groups[2].Value 1))
        }
    }
    $remaining = $script
    foreach ($match in $matches) {
        $remaining = $remaining.Replace($match.Value, "")
    }
    if ($remaining -match '\S') {
        return $false
    }
    $result.Value = @($statuses)
    return $true
}

function Try-NewAuthoritativeEnemy(
    [object]$row,
    [Collections.IDictionary]$enemyCardById,
    [ref]$definition) {
    $definition.Value = $null
    $initialStatuses = @()
    if (-not (Try-GetAuthoritativeEnemyInitialStatuses $row ([ref]$initialStatuses))) {
        return $false
    }
    $intents = @()
    foreach ($cardId in @(([string]$row.CardList -split ",") |
                 ForEach-Object { $_.Trim() } |
                 Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        if (-not $enemyCardById.Contains($cardId)) {
            return $false
        }
        $intent = $null
        if (-not (Try-NewAuthoritativeEnemyIntent `
                $enemyCardById[$cardId] `
                $row `
                ([ref]$intent))) {
            return $false
        }
        $intents += $intent
    }
    if ($intents.Count -eq 0) {
        return $false
    }
    $definition.Value = [ordered]@{
        ownerModId = "Witch"
        enemyId = [string]$row.Id
        displayName = [string]$row.Name
        maxHp = [Math]::Max(1, (Convert-ToInt $row.Hp 1))
        actionCount = [Math]::Max(
            1,
            [Math]::Min(3, (Convert-ToInt $row.ActionCount 1)))
        fidelity = "Authoritative"
        variables = [ordered]@{
            BaseAttack = [Math]::Max(0, (Convert-ToInt $row.Attack 0))
            BaseDefend = [Math]::Max(0, (Convert-ToInt $row.Defend 0))
        }
        initialStatuses = @($initialStatuses)
        intents = @($intents)
    }
    return $true
}

function Try-NewAuthoritativeStaticStatus(
    [object]$row,
    [ref]$result) {
    $modifiers = switch ([string]$row.Id) {
        "buff_vulnerability" { [ordered]@{ AttackedPercentDamage = 0.1 }; break }
        "buff_keenedge" { [ordered]@{ DefaultDamage = 1.0 }; break }
        "buff_weak" { [ordered]@{ DefaultDamage = -1.0 }; break }
        "buff_impregnable" { [ordered]@{ AttackedPercentDamage = -0.1 }; break }
        "buff_extraordinary" { [ordered]@{ PercentDamage = 0.01 }; break }
        "buff_degrade" { [ordered]@{ PercentDamage = -0.1 }; break }
        "buff_resilient" { [ordered]@{ AttackedDefaultDamage = -1.0 }; break }
        "buff_biologicalArmor" { [ordered]@{ ConversionRate = 1.0 }; break }
        default { $null }
    }
    if ($null -eq $modifiers) {
        return $false
    }
    $reducePerTurn = [Math]::Max(0, (Convert-ToInt $row.ReducePerTurn 0))
    $result.Value = [ordered]@{
        ownerModId = "Witch"
        statusId = [string]$row.Id
        displayName = [string]$row.Name
        fidelity = "Authoritative"
        decayAtRoundEnd = $reducePerTurn -gt 0
        reducePerTurn = $reducePerTurn
        reducePerUse = [Math]::Max(0, (Convert-ToInt $row.ReducePerUse 0))
        reducePerAttacked = [Math]::Max(0, (Convert-ToInt $row.ReducePerAttacked 0))
        canRemainAtZero = ([string]$row.CanZero) -eq "TRUE"
        maximumStacks = [Math]::Max(1, (Convert-ToInt $row.UpperBound ([int]::MaxValue)))
        dynamicModifiersPerStack = $modifiers
        triggers = @()
    }
    return $true
}

function New-ConstantExpression([double]$value) {
    return [ordered]@{ operation = "Constant"; constant = $value; arguments = @() }
}

function New-SourceStatusExpression([string]$statusId) {
    return [ordered]@{
        operation = "SourceStatusStacks"
        key = $statusId
        arguments = @()
    }
}

function New-TargetStatusExpression([string]$statusId) {
    return [ordered]@{
        operation = "TargetStatusStacks"
        key = $statusId
        arguments = @()
    }
}

function New-SourceStatusCounterExpression(
    [string]$statusId,
    [string]$counterKey) {
    return [ordered]@{
        operation = "SourceStatusCounter"
        key = "$statusId|$counterKey"
        arguments = @()
    }
}

function New-SourceStatusTagStacksExpression([string]$tag) {
    return [ordered]@{
        operation = "SourceStatusTagStacks"
        key = $tag
        arguments = @()
    }
}

function New-SourceHandExpression([string]$tag = "") {
    return [ordered]@{
        operation = if ([string]::IsNullOrWhiteSpace($tag)) {
            "SourceHandCount"
        } else {
            "SourceHandTagCount"
        }
        key = $tag
        arguments = @()
    }
}

function New-SourceValueExpression([string]$operation) {
    return [ordered]@{ operation = $operation; arguments = @() }
}

function New-ValueExpression([string]$operation, [object[]]$arguments) {
    return [ordered]@{ operation = $operation; arguments = @($arguments) }
}

function New-AuthoritativeStatusBase([object]$row) {
    $reducePerTurn = [Math]::Max(0, (Convert-ToInt $row.ReducePerTurn 0))
    $statusTags = [Collections.Generic.List[string]]::new()
    if ([string]$row.Id -like "buff_ritual*") {
        $statusTags.Add("Ritual")
    }
    return [ordered]@{
        ownerModId = "Witch"
        statusId = [string]$row.Id
        displayName = [string]$row.Name
        fidelity = "Authoritative"
        decayAtRoundEnd = $reducePerTurn -gt 0
        reducePerTurn = $reducePerTurn
        reducePerUse = [Math]::Max(0, (Convert-ToInt $row.ReducePerUse 0))
        reducePerAttacked = [Math]::Max(0, (Convert-ToInt $row.ReducePerAttacked 0))
        canRemainAtZero = ([string]$row.CanZero) -eq "TRUE"
        maximumStacks = [Math]::Max(1, (Convert-ToInt $row.UpperBound ([int]::MaxValue)))
        tags = $statusTags
        dynamicModifiersPerStack = [ordered]@{}
        triggers = @()
    }
}

function New-StatusTrigger(
    [string]$id,
    [string]$eventKind,
    [string]$relation,
    [object[]]$effects,
    [int]$everyNth = 1,
    [int]$minimumEventAmount = [int]::MinValue,
    [string]$requiredActionTag = "",
    [int]$consumeStacks = 0,
    [string]$forbiddenActionTag = "",
    [string]$requiredDefinitionId = "",
    [string]$counterKey = "",
    [string]$counterIncrementMode = "None",
    [int]$counterIncrement = 1,
    [string]$counterFilter = "",
    [int]$minimumCounterValue = [int]::MinValue,
    [int]$maximumCounterValue = [int]::MaxValue,
    [int]$counterStep = 0,
    [int]$counterStepOrigin = 0,
    [bool]$resetCounterAfterTrigger = $false,
    [bool]$removeStatusAfterTrigger = $false) {
    return [ordered]@{
        triggerId = $id
        eventKind = $eventKind
        ownerRelation = $relation
        everyNthEvent = [Math]::Max(1, $everyNth)
        minimumEventAmount = $minimumEventAmount
        requiredActionTag = $requiredActionTag
        forbiddenActionTag = $forbiddenActionTag
        requiredDefinitionId = $requiredDefinitionId
        conditionExpression = $null
        counterKey = $counterKey
        counterIncrementMode = $counterIncrementMode
        counterIncrement = $counterIncrement
        counterFilter = $counterFilter
        minimumCounterValue = $minimumCounterValue
        maximumCounterValue = $maximumCounterValue
        counterStep = $counterStep
        counterStepOrigin = $counterStepOrigin
        resetCounterAfterTrigger = $resetCounterAfterTrigger
        removeStatusAfterTrigger = $removeStatusAfterTrigger
        consumeStacks = $consumeStacks
        effects = @($effects)
    }
}

function Try-NewAuthoritativeTriggeredStatus(
    [object]$row,
    [ref]$result) {
    $id = [string]$row.Id
    $status = New-AuthoritativeStatusBase $row
    $stacks = New-SourceStatusExpression $id
    $selfHp = New-SourceValueExpression "SourceHp"
    $selfMaxHp = New-SourceValueExpression "SourceMaxHp"
    $ritualRepeats = New-ValueExpression "Add" @(
        $stacks,
        (New-SourceStatusExpression "buff_ritualechostaff"))

    switch ($id) {
        "buff_barkhide" {
            $reset = New-StatusTrigger `
                "barkhide-round-start" `
                "TurnStarted" `
                "Any" `
                @()
            $reset.counterKey = "ThisCount"
            $reset.resetCounterAfterTrigger = $true

            $hurt = New-StatusTrigger `
                "barkhide-hurt" `
                "DamageDealt" `
                "EventTarget" `
                @([ordered]@{
                    kind = "GainBlock"
                    target = "Self"
                    definitionId = $id
                    amountExpression = New-ValueExpression "Multiply" @(
                        (New-SourceStatusCounterExpression $id "ThisCount"),
                        (New-ConstantExpression 2))
                })
            $hurt.counterKey = "ThisCount"
            $hurt.counterIncrementMode = "Fixed"
            $status.triggers = @($reset, $hurt)
        }
        "buff_bleeding" {
            $baseLoss = [ordered]@{
                kind = "DirectHpLoss"; target = "Self"; definitionId = $id
                amountExpression = $stacks
            }
            $bonusLoss = [ordered]@{
                kind = "DirectHpLoss"; target = "Self"; definitionId = $id
                amountExpression = $stacks
                conditionExpression = New-ValueExpression "GreaterThan" @(
                    $stacks,
                    (New-ConstantExpression 30))
            }
            $status.triggers = @(New-StatusTrigger "bleeding-action" "ActionStarted" "EventSource" @(
                $baseLoss,
                $bonusLoss))
        }
        "buff_bloodriver" {
            $status.triggers = @(New-StatusTrigger "bloodriver-action" "ActionStarted" "EventSource" @(
                [ordered]@{
                    kind = "EmitEvent"; target = "Self"; amount = 1
                    emittedEventKind = "DiceChecked"; definitionId = $id
                },
                [ordered]@{
                    kind = "AddStatus"; target = "Self"; definitionId = "buff_bleeding"
                    amount = 2; randomChoiceGroup = "bloodriver-roll"
                    randomChoiceWeight = 25.0
                },
                [ordered]@{
                    kind = "AddStatus"; target = "AllOpponents"; definitionId = "buff_bleeding"
                    amount = 4; randomChoiceGroup = "bloodriver-roll"
                    randomChoiceWeight = 75.0
                }))
        }
        "buff_bloodsea" {
            $status.triggers = @(New-StatusTrigger "bloodsea-action" "ActionStarted" "EventSource" @(
                [ordered]@{
                    kind = "AddStatus"; target = "Self"; definitionId = "buff_bleeding"
                    amountExpression = New-ValueExpression "Multiply" @(
                        $stacks,
                        (New-ConstantExpression 3))
                }))
        }
        "buff_bloodwall" {
            $status.triggers = @(New-StatusTrigger "bloodwall-action" "ActionStarted" "EventSource" @(
                [ordered]@{
                    kind = "GainBlock"
                    target = "Self"
                    definitionId = $id
                    amountExpression = $stacks
                }))
        }
        "buff_BonePiercingSpike" {
            $status.triggers = @(New-StatusTrigger "bone-spike-death" "ActorDefeated" "EventTarget" @(
                [ordered]@{
                    kind = "AddStatus"; target = "AllAllies"; definitionId = "buff_bleeding"
                    amountExpression = New-ValueExpression "Multiply" @(
                        (New-SourceStatusExpression "buff_bleeding"),
                        $stacks,
                        (New-ConstantExpression 2))
                }))
        }
        "buff_burn" {
            $status.triggers = @(New-StatusTrigger "burn-round-start" "TurnStarted" "Any" @(
                [ordered]@{
                    kind = "DirectHpLoss"; target = "Self"; definitionId = $id
                    amountExpression = New-ValueExpression "Multiply" @(
                        (New-ValueExpression "Add" @(
                            (New-ConstantExpression 1),
                            (New-ValueExpression "Floor" @(
                                (New-ValueExpression "Divide" @(
                                    $selfHp,
                                    (New-ConstantExpression 100))))))),
                        $stacks)
                }))
        }
        "buff_cripple" {
            $status.triggers = @(New-StatusTrigger "cripple-hurt" "DamageDealt" "EventTarget" @(
                [ordered]@{ kind = "GainEnergy"; target = "Self"; amount = -1 }))
        }
        "buff_cycle" {
            $status.triggers = @(New-StatusTrigger "cycle-shuffle" "DeckShuffled" "EventSource" @(
                [ordered]@{ kind = "GainEnergy"; target = "Self"; amountExpression = $stacks }))
        }
        "buff_fate" {
            $status.triggers = @(New-StatusTrigger "fate-dice-check" "DiceChecked" "EventSource" @(
                [ordered]@{
                    kind = "Damage"; target = "AllOpponents"; definitionId = $id; amount = 2
                }))
        }
        "buff_elements" {
            $status.triggers = @(New-StatusTrigger "elements-action-after" "ActionResolved" "EventSource" @(
                [ordered]@{
                    kind = "AddStatus"; target = "Self"; definitionId = "buff_extraordinary"
                    amountExpression = New-ValueExpression "Multiply" @(
                        $stacks,
                        (New-ConstantExpression 2))
                }))
        }
        "buff_EnergyOverload" {
            $status.dynamicModifiersPerStack = [ordered]@{
                AttackedPercentDamage = 1.5
            }
            $status.triggers = @(New-StatusTrigger "energy-overload-action" "ActionStarted" "EventSource" @(
                [ordered]@{
                    kind = "Heal"; target = "Self"; rounding = "Truncate"
                    amountExpression = New-ValueExpression "Multiply" @(
                        $selfMaxHp,
                        (New-ConstantExpression 0.15))
                }))
        }
        "buff_EnergyStorage" {
            $status.triggers = @(New-StatusTrigger "energy-storage-round-start" "TurnStarted" "Any" @(
                [ordered]@{ kind = "GainEnergy"; target = "Self"; amountExpression = $stacks },
                [ordered]@{ kind = "RemoveStatus"; target = "Self"; definitionId = $id; amount = 1 }))
        }
        "buff_epiphany" {
            $status.dynamicModifiersPerStack = [ordered]@{ BaseEnergy = 1.0 }
        }
        "buff_evergreen" {
            $status.triggers = @(New-StatusTrigger "evergreen-round-start" "TurnStarted" "Any" @(
                [ordered]@{ kind = "Heal"; target = "Self"; amountExpression = $stacks }))
        }
        "buff_frenzy" {
            $status.triggers = @(New-StatusTrigger "frenzy-action" "ActionStarted" "EventSource" @(
                [ordered]@{
                    kind = "AddStatus"; target = "Self"; definitionId = "buff_keenedge"
                    amountExpression = $stacks
                },
                [ordered]@{
                    kind = "DirectHpLoss"; target = "Self"; definitionId = $id
                    amountExpression = New-ValueExpression "Multiply" @(
                        $stacks,
                        (New-ConstantExpression 2))
                    conditionExpression = New-ValueExpression "GreaterThan" @(
                        $selfHp,
                        (New-ValueExpression "Add" @(
                            (New-ValueExpression "Multiply" @(
                                $stacks,
                                (New-ConstantExpression 2))),
                            (New-ConstantExpression 1))))
                }))
        }
        "buff_immortal" {
            $status.triggers = @(New-StatusTrigger "immortal-every-second-hurt" "DamageDealt" "EventTarget" @(
                [ordered]@{ kind = "GainEnergy"; target = "Self"; amountExpression = $stacks }) 2)
        }
        "buff_LilithsPact" {
            $status.dynamicModifiersPerStack = [ordered]@{
                "DirectHpLossTaken.buff_bleeding" = -1.0
            }
        }
        "buff_lifelink" {
            $equalize = [ordered]@{
                kind = "EqualizeHealthByStatus"; target = "Self"
                definitionId = $id; amount = 1
            }
            $added = New-StatusTrigger "lifelink-added" "StatusAdded" "EventTarget" @($equalize)
            $added.requiredDefinitionId = $id
            $status.triggers = @(
                $added,
                (New-StatusTrigger "lifelink-hurt" "DamageDealt" "EventTarget" @($equalize)),
                (New-StatusTrigger "lifelink-healed" "Healed" "EventTarget" @($equalize)))
        }
        "buff_oblivion" {
            $status.triggers = @(New-StatusTrigger "oblivion-every-third-draw" "CardDrawn" "EventTarget" @(
                [ordered]@{ kind = "DiscardRandom"; target = "Self"; amount = 1 }) 3)
        }
        "buff_poised" {
            $status.triggers = @(New-StatusTrigger "poised-hurt" "DamageDealt" "EventTarget" @(
                [ordered]@{
                    kind = "AddStatus"; target = "Self"; definitionId = "buff_counterattack"
                    amountExpression = $stacks
                }))
        }
        "buff_rotten" {
            $status.triggers = @(New-StatusTrigger "rotten-action" "ActionStarted" "EventSource" @(
                [ordered]@{ kind = "SetBlock"; target = "Self"; amount = 0 }))
        }
        "buff_revelation" {
            $trigger = New-StatusTrigger "revelation-non-combo" "ActionStarted" "EventSource" @()
            $trigger.forbiddenActionTag = "Combo"
            $trigger.consumeStacks = 1
            $status.triggers = @($trigger)
        }
        "buff_reverie" {
            $status.triggers = @(New-StatusTrigger "reverie-hurt" "DamageDealt" "EventTarget" @(
                [ordered]@{
                    kind = "CreateRandomCard"; target = "Self"; amount = 1
                    minimumRarity = 2; maximumRarity = 3
                    destinationZone = "DrawPile"; randomizeDestination = $true
                },
                [ordered]@{ kind = "Draw"; target = "Self"; amount = 1 }))
        }
        "buff_ritualenlightenment" {
            $remainingDraws = New-ValueExpression "Subtract" @(
                (New-ConstantExpression 10),
                (New-SourceStatusCounterExpression $id "DrawnCount"))
            $drawAmount = New-ValueExpression "Minimum" @(
                $remainingDraws,
                $ritualRepeats)
            $trigger = New-StatusTrigger "ritual-enlightenment-cost" "CardPlayed" "EventSource" @(
                [ordered]@{
                    kind = "Draw"; target = "Self"; amountExpression = $drawAmount
                },
                [ordered]@{
                    kind = "ModifyStatusCounter"; target = "Self"; definitionId = $id
                    counterKey = "DrawnCount"; amountExpression = $drawAmount
                    counterLimit = 10; removeStatusAtCounterLimit = $true
                }) 1 1
            $trigger.counterKey = "ThisCount"
            $trigger.counterIncrementMode = "Fixed"
            $trigger.minimumCounterValue = 5
            $trigger.counterStep = 3
            $trigger.counterStepOrigin = 5
            $status.triggers = @($trigger)
        }
        "buff_ritualcourage" {
            $countDamage = New-StatusTrigger "ritual-courage-damage" "DamageDealt" "EventSource" @()
            $countDamage.counterKey = "ThisCount"
            $countDamage.counterIncrementMode = "EventAmount"
            $blockAtEnd = New-StatusTrigger "ritual-courage-end" "TurnEnded" "Any" @(
                [ordered]@{
                    kind = "GainBlock"; target = "Self"
                    amountExpression = New-ValueExpression "Multiply" @(
                        (New-SourceStatusCounterExpression $id "ThisCount"),
                        $ritualRepeats)
                })
            $blockAtEnd.removeStatusAfterTrigger = $true
            $status.triggers = @($countDamage, $blockAtEnd)
        }
        "buff_ritualasceticism" {
            $countHand = New-StatusTrigger "ritual-asceticism-end" "TurnEnded" "Any" @()
            $countHand.counterKey = "ThisCount"
            $countHand.counterIncrementMode = "HandCount"
            $resolve = New-StatusTrigger "ritual-asceticism-start" "TurnStarted" "Any" @(
                [ordered]@{
                    kind = "GainEnergy"; target = "Self"
                    amountExpression = New-ValueExpression "Multiply" @(
                        (New-ValueExpression "Floor" @(
                            (New-ValueExpression "Divide" @(
                                (New-SourceStatusCounterExpression $id "ThisCount"),
                                (New-ConstantExpression 2))))),
                        $ritualRepeats)
                })
            $resolve.counterKey = "ThisCount"
            $resolve.minimumCounterValue = 3
            $resolve.removeStatusAfterTrigger = $true
            $status.triggers = @($countHand, $resolve)
        }
        "buff_ritualoverload" {
            $trigger = New-StatusTrigger "ritual-overload-action" "ActionStarted" "EventSource" @(
                [ordered]@{ kind = "DrawToHandLimit"; target = "Self"; amount = 1 })
            $trigger.counterKey = "ThisCount"
            $trigger.counterIncrementMode = "Fixed"
            $trigger.minimumCounterValue = 4
            $trigger.removeStatusAfterTrigger = $true
            $status.triggers = @($trigger)
        }
        "buff_ritualcatalyst" {
            $countRitual = New-StatusTrigger "ritual-catalyst-action" "ActionStarted" "EventSource" @()
            $countRitual.requiredActionTag = "Ritual"
            $countRitual.counterKey = "ThisCount"
            $countRitual.counterIncrementMode = "Fixed"
            $resolve = New-StatusTrigger "ritual-catalyst-start" "TurnStarted" "Any" @(
                [ordered]@{
                    kind = "ModifyStatusCounter"; target = "Self"
                    requiredStatusTag = "Ritual"; counterKey = "ThisCount"
                    amountExpression = $ritualRepeats
                })
            $resolve.counterKey = "ThisCount"
            $resolve.minimumCounterValue = 3
            $status.triggers = @($countRitual, $resolve)
        }
        "buff_ritualechostaff" {
            $status.triggers = @()
        }
        "buff_ritualbloodsacrifice" {
            $markExistingHand = New-StatusTrigger `
                "ritual-blood-sacrifice-mark-hand" `
                "StatusAdded" `
                "EventTarget" `
                @([ordered]@{
                    kind = "AddCardTag"
                    target = "Self"
                    definitionId = "Burnout"
                    sourceZone = "Hand"
                })
            $markExistingHand.requiredDefinitionId = $id

            $markDrawnCard = New-StatusTrigger `
                "ritual-blood-sacrifice-mark-drawn" `
                "CardDrawn" `
                "EventTarget" `
                @([ordered]@{
                    kind = "AddCardTag"
                    target = "Self"
                    definitionId = "Burnout"
                    useEventCard = $true
                })
            $markCreatedCard = New-StatusTrigger `
                "ritual-blood-sacrifice-mark-created" `
                "CardCreated" `
                "EventTarget" `
                @([ordered]@{
                    kind = "AddCardTag"
                    target = "Self"
                    definitionId = "Burnout"
                    useEventCard = $true
                })

            $resolveSacrifice = New-StatusTrigger `
                "ritual-blood-sacrifice-resolve" `
                "CardExhausted" `
                "EventTarget" `
                @([ordered]@{
                    kind = "AddStatus"
                    target = "Self"
                    definitionId = "buff_extraordinary"
                    amountExpression = New-ValueExpression "Multiply" @(
                        (New-ConstantExpression 444),
                        $ritualRepeats)
                })
            $resolveSacrifice.counterKey = "ThisCount"
            $resolveSacrifice.counterIncrementMode = "Fixed"
            $resolveSacrifice.minimumCounterValue = 10
            $resolveSacrifice.maximumCounterValue = 10
            $resolveSacrifice.removeStatusAfterTrigger = $true
            $status.triggers = @(
                $markExistingHand,
                $markDrawnCard,
                $markCreatedCard,
                $resolveSacrifice)
        }
        "buff_ritualtimeprison" {
            $countDeferredEffects = New-StatusTrigger `
                "ritual-time-prison-count" `
                "DeferredEffectTriggered" `
                "EventTarget" `
                @()
            $countDeferredEffects.requiredDefinitionId = "buff_timelock"
            $countDeferredEffects.counterKey = "ThisCount"
            $countDeferredEffects.counterIncrementMode = "EventAmount"

            $retrieveRituals = New-StatusTrigger `
                "ritual-time-prison-retrieve" `
                "TurnStarted" `
                "EventSource" `
                @([ordered]@{
                    kind = "RetrieveCards"
                    target = "Self"
                    requiredCardTag = "Ritual"
                    sourceZone = "DrawPile"
                    destinationZone = "Hand"
                    amountExpression = New-ValueExpression "Multiply" @(
                        (New-SourceStatusCounterExpression $id "ThisCount"),
                        $ritualRepeats)
                })
            $retrieveRituals.counterKey = "ThisCount"
            $retrieveRituals.minimumCounterValue = 1
            $retrieveRituals.resetCounterAfterTrigger = $true
            $status.triggers = @($countDeferredEffects, $retrieveRituals)
        }
        "buff_ritualcycle" {
            $trigger = New-StatusTrigger "ritual-cycle-action" "ActionStarted" "EventSource" @(
                [ordered]@{
                    kind = "GainEnergy"; target = "Self"; amountExpression = $ritualRepeats
                },
                [ordered]@{
                    kind = "Draw"; target = "Self"; amountExpression = $ritualRepeats
                })
            $trigger.requiredActionTag = "Ritual"
            $trigger.counterKey = "ThisCount"
            $trigger.counterIncrementMode = "Fixed"
            $trigger.minimumCounterValue = 4
            $status.triggers = @($trigger)
        }
        "buff_ritualsublimation" {
            $status.triggers = @(New-StatusTrigger "ritual-sublimation-end" "TurnEnded" "Any" @(
                [ordered]@{
                    kind = "WinBattle"; target = "Self"; amount = 1
                    conditionExpression = New-ValueExpression "GreaterThan" @(
                        (New-SourceStatusTagStacksExpression "Ritual"),
                        (New-ConstantExpression 12))
                }))
        }
        "buff_ritualpyre" {
            $targetHp = New-SourceValueExpression "TargetHp"
            $burnTick = New-ValueExpression "Multiply" @(
                $ritualRepeats,
                (New-TargetStatusExpression "buff_burn"),
                (New-ValueExpression "Add" @(
                    (New-ConstantExpression 1),
                    (New-ValueExpression "Floor" @(
                        (New-ValueExpression "Divide" @(
                            $targetHp,
                            (New-ConstantExpression 100))))))))
            $trigger = New-StatusTrigger "ritual-pyre-action" "ActionStarted" "EventSource" @(
                [ordered]@{
                    kind = "DirectHpLoss"; target = "AllOpponents"
                    definitionId = "buff_burn"; amountExpression = $burnTick
                })
            $trigger.requiredActionTag = "BurnReference"
            $trigger.counterKey = "ThisCount"
            $trigger.counterIncrementMode = "Fixed"
            $trigger.minimumCounterValue = 3
            $trigger.counterStep = 3
            $trigger.counterStepOrigin = 3
            $status.triggers = @($trigger)
        }
        "buff_ritualsolidify" {
            $countRetain = New-StatusTrigger "ritual-solidify-end" "TurnEnded" "Any" @()
            $countRetain.counterKey = "ThisCount"
            $countRetain.counterIncrementMode = "HandTagCount"
            $countRetain.counterFilter = "Retain"
            $resolve = New-StatusTrigger "ritual-solidify-start" "TurnStarted" "Any" @(
                [ordered]@{
                    kind = "GainEnergy"; target = "Self"
                    amountExpression = New-ValueExpression "Multiply" @(
                        (New-SourceStatusCounterExpression $id "ThisCount"),
                        $ritualRepeats)
                })
            $resolve.counterKey = "ThisCount"
            $resolve.minimumCounterValue = 1
            $resolve.resetCounterAfterTrigger = $true
            $status.triggers = @($countRetain, $resolve)
        }
        "buff_sourcecast" {
            $status.triggers = @(New-StatusTrigger "sourcecast-every-second-cost" "ActionStarted" "EventSource" @(
                [ordered]@{ kind = "Draw"; target = "Self"; amountExpression = $stacks }) 2 1)
        }
        "buff_SpellNextPower2Draw2" {
            $twiceStacks = New-ValueExpression "Multiply" @(
                $stacks,
                (New-ConstantExpression 2))
            $status.triggers = @(New-StatusTrigger "spell-inspiration-round-start" "TurnStarted" "Any" @(
                [ordered]@{ kind = "GainEnergy"; target = "Self"; amountExpression = $twiceStacks },
                [ordered]@{ kind = "Draw"; target = "Self"; amountExpression = $twiceStacks },
                [ordered]@{ kind = "RemoveStatus"; target = "Self"; definitionId = $id; amount = 1 }))
        }
        "buff_swordIntent" {
            $status.triggers = @(
                (New-StatusTrigger "sword-intent-action-loss" "ActionStarted" "EventSource" @(
                    [ordered]@{
                        kind = "DirectHpLoss"; target = "Self"; definitionId = $id; amount = 1
                        conditionExpression = New-ValueExpression "GreaterThan" @(
                            $selfHp,
                            (New-ConstantExpression 2))
                    })),
                (New-StatusTrigger "sword-intent-every-second-action" "ActionStarted" "EventSource" @(
                    [ordered]@{
                        kind = "AddStatus"; target = "Self"; definitionId = "buff_poised"; amount = 1
                    }) 2))
        }
        "buff_toxin" {
            $status.triggers = @(New-StatusTrigger "toxin-round-end" "TurnEnded" "Any" @(
                [ordered]@{
                    kind = "DirectHpLoss"; target = "Self"; definitionId = $id
                    amountExpression = $stacks
                }))
        }
        "buff_timestop" {
            $status.triggers = @(New-StatusTrigger "timestop-round-start" "TurnStarted" "Any" @(
                [ordered]@{ kind = "SkipTurn"; target = "Self"; amount = 1 }))
        }
        "SpecialBuff_BlessedByHeaven" {
            $trigger = New-StatusTrigger `
                "blessed-by-heaven-round-start" `
                "TurnStarted" `
                "Any" `
                @([ordered]@{
                    kind = "AddStatus"
                    target = "Self"
                    definitionId = "buff_evergreen"
                    rounding = "Floor"
                    amountExpression = New-ValueExpression "Divide" @(
                        $selfMaxHp,
                        (New-ConstantExpression 20))
                })
            $trigger.counterKey = "ThisCount"
            $trigger.counterIncrementMode = "Fixed"
            $trigger.maximumCounterValue = 7
            $status.triggers = @($trigger)
        }
        "SpecialBuff_CAR_Momentum" {
            $status.triggers = @(New-StatusTrigger "car-momentum-round-start" "TurnStarted" "Any" @(
                [ordered]@{
                    kind = "AddStatus"
                    target = "Self"
                    definitionId = "buff_extraordinary"
                    amount = 20
                }))
        }
        "SpecialBuff_Dragon'sBlood" {
            $status.triggers = @(New-StatusTrigger "dragon-blood-round-end" "TurnEnded" "Any" @(
                [ordered]@{
                    kind = "Heal"
                    target = "Self"
                    rounding = "Floor"
                    amountExpression = New-ValueExpression "Divide" @(
                        (New-ValueExpression "Subtract" @(
                            $selfMaxHp,
                            $selfHp)),
                        (New-ConstantExpression 2))
                },
                [ordered]@{
                    kind = "AddStatus"
                    target = "Self"
                    definitionId = "buff_impregnable"
                    amount = 1
                },
                [ordered]@{
                    kind = "AddStatus"
                    target = "Self"
                    definitionId = "buff_extraordinary"
                    amount = 20
                }))
        }
        "SpecialBuff_AllogeneicConcentric" {
            $status.triggers = @(New-StatusTrigger `
                "allogeneic-concentric-ally-death" `
                "ActorDefeated" `
                "EventTargetAllyExceptSelf" `
                @(
                    [ordered]@{
                        kind = "ScaleVariablePercent"
                        target = "Self"
                        definitionId = "PercentDamage"
                        amount = 150
                    },
                    [ordered]@{
                        kind = "ScaleVariablePercent"
                        target = "Self"
                        definitionId = "DefendPercent"
                        amount = 150
                    },
                    [ordered]@{
                        kind = "ScaleMaxHpPercent"
                        target = "Self"
                        definitionId = $id
                        amount = 150
                    },
                    [ordered]@{
                        kind = "SetHpToMax"
                        target = "Self"
                        definitionId = $id
                        amount = 1
                    }))
        }
        "SpecialBuff_believer" {
            $belowHalf = New-ValueExpression "LessThan" @(
                $selfHp,
                (New-ValueExpression "Floor" @(
                    (New-ValueExpression "Divide" @(
                        $selfMaxHp,
                        (New-ConstantExpression 2))))))
            $status.triggers = @(New-StatusTrigger "believer-first-below-half" "DamageDealt" "EventTarget" @(
                [ordered]@{
                    kind = "Heal"
                    target = "Self"
                    definitionId = $id
                    amount = 9999
                },
                [ordered]@{
                    kind = "ScaleVariablePercent"
                    target = "Self"
                    definitionId = "PercentDamage"
                    amount = 130
                },
                [ordered]@{
                    kind = "ScaleVariablePercent"
                    target = "Self"
                    definitionId = "DefendPercent"
                    amount = 130
                }))
            $status.triggers[0].conditionExpression = $belowHalf
            $status.triggers[0].removeStatusAfterTrigger = $true
        }
        "SpecialBuff_expiation" {
            $expire = New-StatusTrigger "expiation-fourth-round" "TurnStarted" "Any" @(
                [ordered]@{
                    kind = "Heal"
                    target = "AllOpponents"
                    definitionId = $id
                    amount = 15
                },
                [ordered]@{
                    kind = "DirectHpLoss"
                    target = "Self"
                    definitionId = $id
                    amount = 9999
                },
                [ordered]@{
                    kind = "WinBattle"
                    target = "Self"
                    definitionId = $id
                    amount = 1
                })
            $expire.counterKey = "ThisCount"
            $expire.counterIncrementMode = "Fixed"
            $expire.minimumCounterValue = 4
            $expire.maximumCounterValue = 4

            $earlyDeath = New-StatusTrigger "expiation-early-death" "ActorDefeated" "EventTarget" @(
                [ordered]@{
                    kind = "DirectHpLoss"
                    target = "AllOpponents"
                    definitionId = $id
                    amount = 15
                })
            $earlyDeath.counterKey = "ThisCount"
            $earlyDeath.maximumCounterValue = 3
            $status.triggers = @($expire, $earlyDeath)
        }
        "SpecialBuff_fluster" {
            $trigger = New-StatusTrigger "fluster-every-third-after-first" "DamageDealt" "EventTarget" @(
                [ordered]@{
                    kind = "DiscardRandom"
                    target = "EventSource"
                    definitionId = $id
                    amount = 1
                })
            $trigger.counterKey = "ThisCount"
            $trigger.counterIncrementMode = "Fixed"
            $trigger.minimumCounterValue = 4
            $trigger.counterStep = 3
            $trigger.counterStepOrigin = 4
            $status.triggers = @($trigger)
        }
        "SpecialBuff_hunting" {
            $playerHand = New-SourceValueExpression "PlayerHandCount"
            $evenHand = New-ValueExpression "Equal" @(
                (New-ValueExpression "Multiply" @(
                    (New-ValueExpression "Floor" @(
                        (New-ValueExpression "Divide" @(
                            $playerHand,
                            (New-ConstantExpression 2))))),
                    (New-ConstantExpression 2))),
                $playerHand)
            $status.triggers = @(New-StatusTrigger "hunting-round-end" "TurnEnded" "Any" @(
                [ordered]@{
                    kind = "Damage"
                    target = "AllOpponents"
                    definitionId = $id
                    rounding = "Truncate"
                    amountExpression = [ordered]@{
                        operation = "SourceVariable"
                        key = "BaseAttack"
                        arguments = @()
                    }
                    conditionExpression = $evenHand
                },
                [ordered]@{
                    kind = "GainBlock"
                    target = "Self"
                    definitionId = $id
                    rounding = "Truncate"
                    amountExpression = [ordered]@{
                        operation = "SourceVariable"
                        key = "BaseDefend"
                        arguments = @()
                    }
                    conditionExpression = New-ValueExpression "Equal" @(
                        $evenHand,
                        (New-ConstantExpression 0))
                }))
        }
        "SpecialBuff_Twins" {
            $trigger = New-StatusTrigger "twins-first-ally-death" "ActorDefeated" "EventTargetAllyExceptSelf" @(
                [ordered]@{
                    kind = "AddStatus"
                    target = "Self"
                    definitionId = "buff_impregnable"
                    amount = 2
                },
                [ordered]@{
                    kind = "AddStatus"
                    target = "Self"
                    definitionId = "buff_extraordinary"
                    amount = 30
                },
                [ordered]@{
                    kind = "AddStatus"
                    target = "Self"
                    definitionId = "buff_thorns"
                    amount = 3
                })
            $trigger.counterKey = "ThisCount"
            $trigger.counterIncrementMode = "Fixed"
            $trigger.minimumCounterValue = 1
            $trigger.maximumCounterValue = 1
            $status.triggers = @($trigger)
        }
        "SpecialBuff_ThirstForBlood" {
            $applyEffects = @(
                [ordered]@{
                    kind = "AddStatus"
                    target = "Self"
                    definitionId = "buff_bloodsea"
                    amount = 1
                },
                [ordered]@{
                    kind = "AddStatus"
                    target = "AllOpponents"
                    definitionId = "buff_bloodriver"
                    amount = 3
                })
            $added = New-StatusTrigger `
                "thirst-for-blood-added" `
                "StatusAdded" `
                "EventTarget" `
                $applyEffects
            $added.requiredDefinitionId = $id
            $status.triggers = @(
                (New-StatusTrigger "thirst-for-blood-battle-start" "BattleStarted" "Any" $applyEffects),
                (New-StatusTrigger "thirst-for-blood-summoned" "ActorSummoned" "EventTarget" $applyEffects),
                $added)
        }
        "SpecialBuff_Transcendent" {
            $status.dynamicModifiersPerStack = [ordered]@{
                AttackedDefaultDamage = -4.0
                AttackedPercentDamage = -0.3
                PercentDamage = 0.3
            }
            $status.triggers = @(New-StatusTrigger "transcendent-action" "ActionStarted" "EventSource" @(
                [ordered]@{
                    kind = "DirectHpLoss"
                    target = "AllOpponents"
                    definitionId = $id
                    amount = 4
                }))
        }
        "SpecialBuff_UnparalleledPower" {
            $trigger = New-StatusTrigger "unparalleled-power-round-end" "TurnEnded" "Any" @(
                [ordered]@{
                    kind = "DirectHpLoss"
                    target = "Self"
                    definitionId = $id
                    rounding = "Floor"
                    amountExpression = New-ValueExpression "Divide" @(
                        $selfMaxHp,
                        (New-ConstantExpression 4))
                },
                [ordered]@{
                    kind = "ScaleVariablePercent"
                    target = "Self"
                    definitionId = "PercentDamage"
                    amount = 200
                })
            $trigger.counterKey = "ThisCount"
            $trigger.counterIncrementMode = "Fixed"
            $trigger.maximumCounterValue = 2
            $status.triggers = @($trigger)
        }
        "buff_unyielding" {
            $status.triggers = @(New-StatusTrigger "unyielding-every-second-hurt" "DamageDealt" "EventTarget" @(
                [ordered]@{ kind = "Draw"; target = "Self"; amountExpression = $stacks }) 2)
        }
        "buff_vitality" {
            $status.triggers = @(New-StatusTrigger "vitality-hurt" "DamageDealt" "EventTarget" @(
                [ordered]@{ kind = "GainBlock"; target = "Self"; amountExpression = $stacks }))
        }
        "buff_counterattack" {
            $damage = [ordered]@{
                kind = "Damage"; target = "EventSource"; definitionId = $id
                amountExpression = $stacks
            }
            $status.triggers = @(
                (New-StatusTrigger "counterattack-attack" "ActionStarted" "EventTarget" @($damage) 1 ([int]::MinValue) "Attack" ([int]::MaxValue)),
                (New-StatusTrigger "counterattack-skill" "ActionStarted" "EventTarget" @($damage) 1 ([int]::MinValue) "Skill" ([int]::MaxValue)))
        }
        default {
            return $false
        }
    }
    $result.Value = $status
    return $true
}

$resolvedExport = (Resolve-Path -LiteralPath $TableExport).Path
$tables = (Get-Content -Raw -Encoding UTF8 -LiteralPath $resolvedExport | ConvertFrom-Json).tables
$levels = @($tables.Level | Where-Object { Test-BaseGameId ([string]$_.Id) })
$enemies = @($tables.Enemy | Where-Object { Test-BaseGameId ([string]$_.Id) })
$enemyCards = @($tables.EnemyCard | Where-Object { Test-BaseGameId ([string]$_.Id) })
$enemyCardById = @{}
foreach ($enemyCard in $enemyCards) {
    $enemyCardById[[string]$enemyCard.Id] = $enemyCard
}
$cards = @($tables.Card | Where-Object {
    (Test-BaseGameId ([string]$_.Id)) `
        -and ([string]$_.Type) -ne "诅咒" `
        -and -not [string]::IsNullOrWhiteSpace(([string]$_.PackBelong)) `
        -and ([string]$_.PackBelong) -ne "cardpack_13"
})
$relics = @($tables.Relic | Where-Object { Test-BaseGameId ([string]$_.Id) })
$blessings = @($tables.Bless | Where-Object { Test-BaseGameId ([string]$_.Id) })
$buffs = @($tables.Buff | Where-Object { Test-BaseGameId ([string]$_.Id) })

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
        offerWeight = switch ($tier) {
            1 { 8.0 }
            2 { 5.0 }
            3 { 2.0 }
            default { 1.0 }
        }
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
            stacks = [Math]::Max(1, (Convert-ToInt $_.MaxCount 1))
            combatRelevant = $combatRelevant
            implemented = $id -in @(
                "Hard_1", "Hard_2", "Hard_3", "Hard_4", "Hard_6", "Hard_9",
                "Hard_10", "Hard_11", "Hard_14", "Hard_15", "Hard_16",
                "Hard_17", "Hard_18", "Hard_19", "Hard_20", "Hard_21",
                "Hard_22")
        }
    })
$hardTagDiff = [int](($hardAffixes | ForEach-Object {
    $row = $tables.Hard | Where-Object Id -eq $_.affixId | Select-Object -First 1
    (Convert-ToInt $row.Level 0) * $_.stacks
} | Measure-Object -Sum).Sum)
$hardHpStacks = [int](($hardAffixes |
    Where-Object affixId -eq "Hard_3" |
    ForEach-Object { [int]$_["stacks"] } |
    Measure-Object -Sum).Sum)
$hardAttackStacks = [int](($hardAffixes |
    Where-Object affixId -eq "Hard_4" |
    ForEach-Object { [int]$_["stacks"] } |
    Measure-Object -Sum).Sum)
$hardHighLevelHpStacks = [int](($hardAffixes |
    Where-Object affixId -eq "Hard_20" |
    ForEach-Object { [int]$_["stacks"] } |
    Measure-Object -Sum).Sum)
$hardDreamCurseStacks = [int](($hardAffixes |
    Where-Object affixId -eq "Hard_19" |
    ForEach-Object { [int]$_["stacks"] } |
    Measure-Object -Sum).Sum)

$campaign = [ordered]@{
    schemaVersion = 2
    campaignId = "witch.world-simulation.standard-v2"
    campaignVersion = "2.1.0"
    rulesetVersion = "witch-base-evaluation-v2"
    initialMoney = 100
    player = [ordered]@{
        roleId = "career_1"
        maxHp = 100
        currentHp = 100
        baseEnergy = 3
        deck = @(
            "card_1", "card_2", "card_1", "card_2",
            "card_1", "card_2", "card_2", "burningcard_1",
            "card_4", "card_3", "burningcard_2",
            # Normal mode adds two cards for each selected attribute.
            # The standard simulation fixes Strength as main and Wisdom
            # as secondary, matching GameEntryUI.NormalGame.
            "burningcard_2", "elementscard_9",
            "card_3", "elementscard_1")
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
            movePlayedCardAfterResolution = $false
            initialDiscardCards = @()
            directHpLossAfterPlayerCard = 0
            additionalEnemyHpMultiplierMinimumGameLevel = 2147483647
            additionalEnemyHpMultiplier = 1.0
            playerVariables = [ordered]@{
                Difficulty = 1
                TagDiff = 0
            }
            enemyVariables = [ordered]@{
                Difficulty = 1
                TagDiff = 0
            }
            enemyInitialStatuses = @()
            hardAffixes = @()
        },
        [ordered]@{
            difficultyId = "advanced"
            displayName = "高级难度（本体满词条）"
            enemyHpMultiplier = 1.0 + 0.1 * $hardHpStacks
            enemyAttackMultiplier = 1.0 + 0.1 * $hardAttackStacks
            applyGameLevelShield = $true
            movePlayedCardAfterResolution = $true
            initialDiscardCards = @(
                1..$hardDreamCurseStacks | ForEach-Object { "cursecard_11" })
            directHpLossAfterPlayerCard = 1
            additionalEnemyHpMultiplierMinimumGameLevel = 18
            additionalEnemyHpMultiplier =
                1.0 + 1.0 * $hardHighLevelHpStacks
            playerVariables = [ordered]@{
                Difficulty = 1 + (($hardAffixes |
                    Where-Object affixId -eq "Hard_5" |
                    ForEach-Object { [int]$_["stacks"] } |
                    Measure-Object -Sum).Sum)
                TagDiff = $hardTagDiff
                LateThrow = 1
            }
            enemyVariables = [ordered]@{
                Difficulty = 1 + (($hardAffixes |
                    Where-Object affixId -eq "Hard_5" |
                    ForEach-Object { [int]$_["stacks"] } |
                    Measure-Object -Sum).Sum)
                TagDiff = $hardTagDiff
            }
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
    retainBlockBetweenTurns = $true
    requireAuthoritativeRules = $false
    # Run with a full in-memory trace, then AuraToolsExp retains only the
    # final-boss trace or the first failing battle for the batch report.
    traceLevel = "Full"
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
$requiredStartingCardIds = @(
    "card_1", "card_2", "card_3", "card_4", "burningcard_1", "burningcard_2")
$requiredMoneyCardIds = @("card_5", "luckycard_3", "luckycard_10")
$rulesetCardRows = @($cards + @($tables.Card | Where-Object {
    (Test-BaseGameId ([string]$_.Id)) -and (
        ([string]$_.Id) -in $requiredStartingCardIds `
            -or ([string]$_.Id) -in $requiredMoneyCardIds `
            -or ([string]$_.Type) -eq "诅咒")
}) | Group-Object Id | ForEach-Object { $_.Group[0] })
$rulesetCards = @($rulesetCardRows | ForEach-Object {
    $moneyCard = $null
    if (Try-NewAuthoritativeCurseCard $_ ([ref]$moneyCard)) {
        $moneyCard
    } elseif (Try-NewAuthoritativeDeterministicCard $_ ([ref]$moneyCard)) {
        $moneyCard
    } elseif (Try-NewAuthoritativeMoneyCard $_ ([ref]$moneyCard)) {
        $moneyCard
    } elseif ($authoritativeCards.ContainsKey([string]$_.Id)) {
        $existing = $authoritativeCards[[string]$_.Id]
        $existingTags = @(([string]$_.Tag -split "\||,|，|;|；") |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Unique)
        $existing | Add-Member -NotePropertyName tags -NotePropertyValue $existingTags -Force
        if ($existingTags -contains "Burnout" -or $existingTags -contains "Fragmented") {
            $existing.exhaust = $true
        }
        $existing
    } else {
        $direct = $null
        if (Try-NewTaggedRetrievalCard $_ ([ref]$direct)) {
            $direct
        } elseif (Try-NewDirectAuthoritativeCard $_ ([ref]$direct)) {
            $direct
        } else {
            New-ApproximateCard $_
        }
    }
})
$rulesetEnemies = @($enemies | ForEach-Object {
    $authoritativeEnemy = $null
    if (Try-NewAuthoritativeEnemy $_ $enemyCardById ([ref]$authoritativeEnemy)) {
        $authoritativeEnemy
    } else {
        New-ApproximateEnemy $_ $enemyCardById
    }
})
$rulesetStatuses = @($buffs | ForEach-Object {
    $directStatus = $null
    if (Try-NewAuthoritativeTriggeredStatus $_ ([ref]$directStatus)) {
        $directStatus
    } elseif (Try-NewAuthoritativeStaticStatus $_ ([ref]$directStatus)) {
        $directStatus
    } else {
        $approximateStatusTags = [Collections.Generic.List[string]]::new()
        if ([string]$_.Id -like "buff_ritual*") {
            $approximateStatusTags.Add("Ritual")
        }
        $approximateStatus = [ordered]@{
            ownerModId = "Witch"
            statusId = [string]$_.Id
            displayName = [string]$_.Name
            fidelity = "Approximate"
            decayAtRoundEnd = $false
            reducePerTurn = 0
            maximumStacks = [Math]::Max(
                1,
                (Convert-ToInt $_.UpperBound ([int]::MaxValue)))
            tags = $approximateStatusTags
            triggers = @()
        }
        $approximateStatus
    }
})
$ruleset = [ordered]@{
    version = "witch-base-evaluation-v2"
    cards = $rulesetCards
    enemies = $rulesetEnemies
    statuses = $rulesetStatuses
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
