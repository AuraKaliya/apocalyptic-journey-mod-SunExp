param(
    [switch]$UpdateNativeIntentCosts
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$growthPath = Join-Path $repoRoot "Terrias/spirit.growth.registry.json"
$intentPath = Join-Path $repoRoot "Terrias/spirit.intent.registry.json"
$catalogPath = Join-Path $repoRoot "docs/Terrias/design/04-游戏主体精灵种族值表.csv"
$outputPath = Join-Path $repoRoot "Terrias/spirit.training.registry.json"
$baseGameExportDirectory = Join-Path $repoRoot "docs/游戏主体内容/combat-knowledge/table-exports"
$terriasEnemyCardTextPath = Join-Path $repoRoot "Terrias/Text/EnemyCard/terrias.csv"

function Target([string]$scope, [string]$mode, [string]$policy) {
    return [ordered]@{ scope = $scope; mode = $mode; policy = $policy }
}

function Effect(
    [string]$handler,
    [hashtable]$target,
    [int]$flat = 0,
    [double]$attack = 0,
    [double]$armor = 0,
    [double]$magic = 0,
    [double]$speed = 0,
    [string]$buff = "",
    [int]$stacks = 0,
    [int]$hits = 1,
    [int]$display = 1) {
    return [ordered]@{
        handlerId = $handler; target = $target; hitCount = $hits
        buffId = $buff; buffStacks = $stacks; flatValue = $flat
        attackScale = $attack; armorScale = $armor; magicScale = $magic; speedScale = $speed
        displayIndex = $display
    }
}

function Intent(
    [string]$id,
    [string]$card,
    [string]$pool,
    [string]$name,
    [string]$description,
    [string]$type,
    [int]$cost,
    [int]$cooldown,
    [int]$priority,
    [object[]]$effects,
    [string]$eligibility = "") {
    $primary = $effects[0]
    return [ordered]@{
        id = $id; enemyCardId = "Terrias_terrias_enemycard_spirit_common_$card"
        pool = $pool; adaptationNote = "Terrias universal spirit training pool"
        displayName = $name; description = $description; type = $type
        cost = $cost; cooldown = $cooldown; basePriority = $priority
        handlerId = $primary.handlerId; target = $primary.target; hitCount = $primary.hitCount
        buffId = $primary.buffId; buffStacks = $primary.buffStacks; flatValue = $primary.flatValue
        attackScale = $primary.attackScale; armorScale = $primary.armorScale
        magicScale = $primary.magicScale; speedScale = $primary.speedScale
        eligibilityPolicy = $eligibility; priorityBonus = ""
        threat = [ordered]@{ preview = 0; onUse = 0; decay = 4 }
        effects = $effects
    }
}

function Number-Text([double]$value) {
    return $value.ToString("0.##", [Globalization.CultureInfo]::InvariantCulture)
}

function Numeric-Formula([object]$effect) {
    $parts = New-Object System.Collections.Generic.List[string]
    if ([int]$effect.flatValue -ne 0) { $parts.Add(([int]$effect.flatValue).ToString()) }
    if ([double]$effect.attackScale -ne 0) { $parts.Add("攻击×$(Number-Text ([double]$effect.attackScale))") }
    if ([double]$effect.armorScale -ne 0) { $parts.Add("护甲×$(Number-Text ([double]$effect.armorScale))") }
    if ([double]$effect.magicScale -ne 0) { $parts.Add("最大魔能×$(Number-Text ([double]$effect.magicScale))") }
    if ([double]$effect.speedScale -ne 0) { $parts.Add("速度×$(Number-Text ([double]$effect.speedScale))") }
    if ($parts.Count -eq 0) { return "1" }
    return $parts -join "+"
}

function Target-Text([object]$target) {
    if ($null -eq $target) { return "目标" }
    $scope = [string]$target.scope
    $mode = [string]$target.mode
    $policy = [string]$target.policy
    if ($scope -eq "Self" -or $policy -eq "self" -or $policy -eq "friendly.owner_or_self_defense") { return "自身" }
    if ($scope -eq "Enemy") {
        if ($mode -eq "All") { return "所有敌人" }
        return "目标敌人"
    }
    if ($scope -eq "Friendly") {
        if ($mode -eq "All") { return "所有友方" }
        return "目标友方"
    }
    return "目标"
}

function Buff-Text([string]$buffId) {
    $value = switch ($buffId) {
        "buff_bleeding" { "流血" }
        "buff_burn" { "灼烧" }
        "buff_cripple" { "残废" }
        "buff_degrade" { "衰退" }
        "buff_evergreen" { "自愈" }
        "buff_extraordinary" { "超凡" }
        "buff_frenzy" { "狂暴" }
        "buff_impregnable" { "坚毅" }
        "buff_keenedge" { "锋锐" }
        "buff_lifelink" { "生命链接" }
        "buff_oblivion" { "遗忘" }
        "buff_rotten" { "腐烂" }
        "buff_timestop" { "时停" }
        "buff_toxin" { "毒素" }
        "buff_vitality" { "活力" }
        "buff_vulnerability" { "易伤" }
        "buff_weak" { "无力" }
        "Terrias_terrias_body_burn" { "躯体灼烧" }
        "Terrias_terrias_gathered_flame" { "聚集之火" }
        default { $buffId }
    }
    return $value
}

function Native-Description([object]$intent, [object]$sourceRow) {
    $effects = @($intent.effects)
    if ($effects.Count -eq 0) { $effects = @($intent) }
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($effect in $effects) {
        $handler = [string]$effect.handlerId
        $target = Target-Text $effect.target
        $formula = Numeric-Formula $effect
        $hits = [Math]::Max(1, [int]$effect.hitCount)
        switch -Wildcard ($handler) {
            "damage.*" {
                $prefix = if ($hits -gt 1) { "连续${hits}次，每次" } else { "" }
                $parts.Add("对$target${prefix}造成${formula}点伤害")
            }
            "block.*" { $parts.Add("为${target}获得${formula}点护盾") }
            "heal.*" { $parts.Add("为${target}恢复${formula}点生命") }
            "buff.apply" {
                $stacks = [Math]::Max(1, [int]$effect.buffStacks)
                $parts.Add("为${target}施加${stacks}层$(Buff-Text ([string]$effect.buffId))")
            }
            default {
                if ($null -ne $sourceRow -and -not [string]::IsNullOrWhiteSpace([string]$sourceRow.Description)) {
                    $parts.Add(([string]$sourceRow.Description).Replace("{0}", "相应数值").TrimEnd('。'))
                }
            }
        }
    }
    if ($parts.Count -eq 0) { return "按精灵当前属性执行该固有意图。" }
    return ($parts -join "；") + "。"
}

$enemyLow = Target "Enemy" "Single" "enemy.lowest_hp"
$enemyWeak = Target "Enemy" "Single" "enemy.lowest_buff_then_lowest_hp"
$enemyAll = Target "Enemy" "All" "enemy.all"
$self = Target "Self" "Single" "self"
$selfDefense = Target "Friendly" "Single" "friendly.owner_or_self_defense"
$friendlyWounded = Target "Friendly" "Single" "friendly.most_wounded"
$friendlyGuard = Target "Friendly" "Single" "friendly.lowest_block_then_hp"
$friendlyAll = Target "Friendly" "All" "friendly.all"

$commonIntents = New-Object System.Collections.Generic.List[object]
$commonIntents.Add((Intent -id "spirit.common.basic.probing-strike.intent" -card "probing_strike" -pool "Common.Basic" -name "试探突击" -description "对生命最低的敌人造成伤害。" -type "Attack" -cost 1 -cooldown 0 -priority 14 -effects @(
    (Effect -handler "damage.single" -target $enemyLow -flat 2 -attack 0.65))))
$commonIntents.Add((Intent -id "spirit.common.basic.temporary-ward.intent" -card "temporary_ward" -pool "Common.Basic" -name "临时屏障" -description "为自身获得护盾。" -type "Defense" -cost 1 -cooldown 0 -priority 13 -effects @(
    (Effect -handler "block.single" -target $selfDefense -flat 3 -armor 0.70))))
$commonIntents.Add((Intent -id "spirit.common.basic.emergency-heal.intent" -card "emergency_heal" -pool "Common.Basic" -name "应急治愈" -description "为生命比例最低的受伤友方恢复生命。" -type "Recovery" -cost 2 -cooldown 1 -priority 15 -effects @(
    (Effect -handler "heal.single" -target $friendlyWounded -flat 3 -magic 0.50))))
$commonIntents.Add((Intent -id "spirit.common.basic.focused-chant.intent" -card "focused_chant" -pool "Common.Basic" -name "集中咏唱" -description "下一次直接数值意图提高40%。" -type "Support" -cost 1 -cooldown 1 -priority 10 -effects @(
    (Effect -handler "numeric.prepare" -target $self -flat 40 -buff "focused-chant" -stacks 1)) -eligibility "no-pending-numeric-bonus"))
$commonIntents.Add((Intent -id "spirit.common.basic.weakening-mark.intent" -card "weakening_mark" -pool "Common.Basic" -name "虚弱咒痕" -description "对一名敌人施加1层虚弱。" -type "Interference" -cost 1 -cooldown 1 -priority 11 -effects @(
    (Effect -handler "buff.apply" -target $enemyWeak -buff "buff_weak" -stacks 1))))

$commonIntents.Add((Intent -id "spirit.common.tactical.breaking-strike.intent" -card "breaking_strike" -pool "Common.Tactical" -name "破势一击" -description "造成伤害并施加1层易伤。" -type "Attack" -cost 2 -cooldown 1 -priority 17 -effects @(
    (Effect -handler "damage.single" -target $enemyLow -flat 3 -attack 0.75 -display 1),
    (Effect -handler "buff.apply" -target $enemyLow -buff "buff_vulnerability" -stacks 1 -display 2))))
$commonIntents.Add((Intent -id "spirit.common.tactical.guardian-ward.intent" -card "guardian_ward" -pool "Common.Tactical" -name "援护结界" -description "为护盾最低的友方获得护盾。" -type "Defense" -cost 2 -cooldown 1 -priority 16 -effects @(
    (Effect -handler "block.single" -target $friendlyGuard -flat 4 -armor 0.90))))
$commonIntents.Add((Intent -id "spirit.common.tactical.life-stream.intent" -card "life_stream" -pool "Common.Tactical" -name "生命涓流" -description "立即治疗，并在目标下次行动结束时再次治疗。" -type "Recovery" -cost 2 -cooldown 2 -priority 18 -effects @(
    (Effect -handler "heal.single" -target $friendlyWounded -flat 4 -magic 0.65 -display 1),
    (Effect -handler "heal.delayed" -target $friendlyWounded -flat 3 -buff "life-stream" -stacks 1 -display 2))))
$commonIntents.Add((Intent -id "spirit.common.tactical.magic-reflow.intent" -card "magic_reflow" -pool "Common.Tactical" -name "魔能回流" -description "主动恢复2点魔能。" -type "Support" -cost 0 -cooldown 2 -priority 12 -effects @(
    (Effect -handler "magic.recover" -target $self -flat 2)) -eligibility "missing-magic-at-least-2"))
$commonIntents.Add((Intent -id "spirit.common.tactical.armor-break-mark.intent" -card "armor_break_mark" -pool "Common.Tactical" -name "破甲标记" -description "对一名敌人施加2层易伤。" -type "Interference" -cost 2 -cooldown 1 -priority 15 -effects @(
    (Effect -handler "buff.apply" -target $enemyWeak -buff "buff_vulnerability" -stacks 2))))

$commonIntents.Add((Intent -id "spirit.common.advanced.swift-pierce.intent" -card "swift_pierce" -pool "Common.Advanced" -name "迅捷穿刺" -description "以速度为额外基值造成单体伤害。" -type "Attack" -cost 3 -cooldown 1 -priority 20 -effects @(
    (Effect -handler "damage.single" -target $enemyLow -flat 3 -attack 0.60 -speed 0.08))))
$commonIntents.Add((Intent -id "spirit.common.advanced.astral-veil.intent" -card "astral_veil" -pool "Common.Advanced" -name "群星护幕" -description "为所有友方获得护盾。" -type "Defense" -cost 3 -cooldown 2 -priority 19 -effects @(
    (Effect -handler "block.all" -target $friendlyAll -flat 2 -armor 0.45))))
$commonIntents.Add((Intent -id "spirit.common.advanced.life-echo.intent" -card "life_echo" -pool "Common.Advanced" -name "生命回响" -description "为所有友方恢复生命。" -type "Recovery" -cost 3 -cooldown 2 -priority 20 -effects @(
    (Effect -handler "heal.all" -target $friendlyAll -flat 2 -magic 0.35)) -eligibility "life-echo-needed"))
$commonIntents.Add((Intent -id "spirit.common.advanced.overflow-conversion.intent" -card "overflow_conversion" -pool "Common.Advanced" -name "余能转化" -description "魔能全满时，使接下来2次直接数值意图提高25%。" -type "Support" -cost 2 -cooldown 2 -priority 14 -effects @(
    (Effect -handler "numeric.prepare" -target $self -flat 25 -buff "overflow-conversion" -stacks 2)) -eligibility "full-magic-and-no-overflow"))
$commonIntents.Add((Intent -id "spirit.common.advanced.lockdown-seal.intent" -card "lockdown_seal" -pool "Common.Advanced" -name "封锁刻印" -description "对所有敌人各施加1层虚弱与易伤。" -type "Interference" -cost 3 -cooldown 2 -priority 21 -effects @(
    (Effect -handler "buff.apply" -target $enemyAll -buff "buff_weak" -stacks 1 -display 1),
    (Effect -handler "buff.apply" -target $enemyAll -buff "buff_vulnerability" -stacks 1 -display 2))))

$passives = @(
    [ordered]@{ id="spirit.passive.common.core.opening-calibration"; displayName="先发校准"; description="每场战斗第一次直接数值意图提高25%。"; pool="Common.Core"; effectKind="opening-calibration"; intentType=""; numericBonusPercent=25 },
    [ordered]@{ id="spirit.passive.common.core.exploit-opening"; displayName="乘隙追击"; description="单体伤害命中带有负面状态的目标时提高15%。"; pool="Common.Core"; effectKind="exploit-opening"; intentType="Attack"; numericBonusPercent=15 },
    [ordered]@{ id="spirit.passive.common.core.emergency-barrier"; displayName="应急屏障"; description="每场战斗首次降至40%生命时获得一次护盾。"; pool="Common.Core"; effectKind="emergency-barrier"; intentType="Defense"; numericBonusPercent=0 },
    [ordered]@{ id="spirit.passive.common.core.stable-structure"; displayName="稳定结构"; description="行动规划前若没有护盾，获得少量护盾；自身回合冷却2。"; pool="Common.Core"; effectKind="stable-structure"; intentType="Defense"; numericBonusPercent=0 },
    [ordered]@{ id="spirit.passive.common.core.efficient-casting"; displayName="节能施术"; description="每场战斗第一次释放基础消耗不少于2的意图时少消耗1点魔能。"; pool="Common.Core"; effectKind="efficient-casting"; intentType="Support"; numericBonusPercent=0 },
    [ordered]@{ id="spirit.passive.common.core.recovery-loop"; displayName="回能循环"; description="被迫等待时额外恢复1点魔能。"; pool="Common.Core"; effectKind="recovery-loop"; intentType="Support"; numericBonusPercent=0 },
    [ordered]@{ id="spirit.passive.common.core.alternating-tactics"; displayName="交替战术"; description="本次非等待意图类型与上次不同时，直接数值提高15%。"; pool="Common.Core"; effectKind="alternating-tactics"; intentType=""; numericBonusPercent=15 },
    [ordered]@{ id="spirit.passive.common.core.guardian-contract"; displayName="守护契约"; description="主人受到伤害后，下一次防御或恢复意图提高25%。"; pool="Common.Core"; effectKind="guardian-contract"; intentType="Defense"; numericBonusPercent=25 },
    [ordered]@{ id="spirit.passive.common.advanced.mana-tide"; displayName="盈缺律"; description="魔能为0时行动前恢复1；魔能全满时本回合直接数值提高20%。"; pool="Common.Advanced"; effectKind="mana-tide"; intentType="Support"; numericBonusPercent=20 },
    [ordered]@{ id="spirit.passive.common.advanced.desperate-echo"; displayName="绝境回响"; description="生命不高于30%时，直接数值提高30%。"; pool="Common.Advanced"; effectKind="desperate-echo"; intentType=""; numericBonusPercent=30 },
    [ordered]@{ id="spirit.passive.common.advanced.swift-calculation"; displayName="迅捷演算"; description="速度倍率贡献提高50%，但不改变速度与行动顺序。"; pool="Common.Advanced"; effectKind="swift-calculation"; intentType="Attack"; numericBonusPercent=0 },
    [ordered]@{ id="spirit.passive.common.advanced.combo-resonance"; displayName="连携余韵"; description="使用辅助或恢复意图后，下一次攻击或防御直接数值提高30%。"; pool="Common.Advanced"; effectKind="combo-resonance"; intentType=""; numericBonusPercent=30 }
)

$growth = Get-Content -LiteralPath $growthPath -Raw | ConvertFrom-Json
$intentRegistry = Get-Content -LiteralPath $intentPath -Raw | ConvertFrom-Json
$catalog = Import-Csv -LiteralPath $catalogPath
$baseGameExport = Get-ChildItem -LiteralPath $baseGameExportDirectory -Filter "witch-tables-*.json" |
    Sort-Object Name -Descending | Select-Object -First 1
$baseEnemyCards = @{}
if ($null -ne $baseGameExport) {
    $baseGameTables = Get-Content -LiteralPath $baseGameExport.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($row in @($baseGameTables.Tables.EnemyCard)) { $baseEnemyCards[[string]$row.Id] = $row }
}
foreach ($row in @(Import-Csv -LiteralPath $terriasEnemyCardTextPath)) {
    if ([string]::IsNullOrWhiteSpace([string]$row.Id) -or [string]$row.Id -eq "唯一标识") { continue }
    $baseEnemyCards[[string]$row.Id] = $row
    $baseEnemyCards["Terrias_terrias_$([string]$row.Id)"] = $row
}
$displayNames = @{}
foreach ($row in $catalog) { $displayNames[$row.profileId] = $row.displayName }
$intentById = @{}
foreach ($intent in $intentRegistry.intents) {
    if ([string]$intent.pool -eq "Pve") {
        $sourceRow = if ($baseEnemyCards.ContainsKey([string]$intent.enemyCardId)) { $baseEnemyCards[[string]$intent.enemyCardId] } else { $null }
        if ($null -eq $intent.PSObject.Properties["displayName"]) { $intent | Add-Member -NotePropertyName displayName -NotePropertyValue "" }
        if ($null -eq $intent.PSObject.Properties["description"]) { $intent | Add-Member -NotePropertyName description -NotePropertyValue "" }
        if ($null -ne $sourceRow -and -not [string]::IsNullOrWhiteSpace([string]$sourceRow.Name)) {
            $intent.displayName = [string]$sourceRow.Name
        }
        $intent.description = Native-Description $intent $sourceRow
    }
    $intentById[$intent.id] = $intent
}
$intentProfileById = @{}
foreach ($profile in $intentRegistry.profiles) { $intentProfileById[$profile.profileId] = $profile }

function Select-Defaults([object]$profile) {
    if ($null -eq $profile) { return @() }
    $all = @($profile.pveAttackTendency) + @($profile.pveDefenseTendency) | Select-Object -Unique
    $selected = New-Object System.Collections.Generic.List[string]
    $seenTypes = @{}
    foreach ($id in $all) {
        $type = if ($intentById.ContainsKey($id)) { [string]$intentById[$id].type } else { "" }
        if (-not $seenTypes.ContainsKey($type)) {
            $selected.Add([string]$id); $seenTypes[$type] = $true
        }
        if ($selected.Count -ge 3) { break }
    }
    foreach ($id in $all) {
        if ($selected.Count -ge 3) { break }
        if (-not $selected.Contains([string]$id)) { $selected.Add([string]$id) }
    }
    return @($selected)
}

$speciesProfiles = New-Object System.Collections.Generic.List[object]
$speciesPassives = @{}
foreach ($profile in $growth.profiles) {
    $profileId = [string]$profile.profileId
    $speciesId = [string]$profile.speciesId
    $intentProfile = if ($intentProfileById.ContainsKey($profileId)) { $intentProfileById[$profileId] } else { $null }
    $defaults = @(Select-Defaults $intentProfile)
    if ($profileId -eq "base-game.10040") {
        $preferred = @(
            "spirit.pve.enemycard_superfireball.intent",
            "spirit.pve.enemycard_weaklight.intent",
            "spirit.pve.enemycard_rejuvenation.intent") | Where-Object { $intentById.ContainsKey($_) }
        if ($preferred.Count -gt 0) { $defaults = $preferred }
    }
    $passiveId = "spirit.passive.species.$(($speciesId.ToLowerInvariant() -replace '[^a-z0-9]+','-').Trim('-')).inherent"
    if (-not $speciesPassives.ContainsKey($speciesId)) {
        $dominantType = if ($defaults.Count -gt 0 -and $intentById.ContainsKey($defaults[0])) { [string]$intentById[$defaults[0]].type } else { "Attack" }
        $name = if ($displayNames.ContainsKey($profileId)) { [string]$displayNames[$profileId] } else { $speciesId }
        $speciesPassives[$speciesId] = [ordered]@{
            id=$passiveId; displayName="$name·本能"
            description="使用$dominantType 类型的直接数值意图时，数值提高10%。"
            pool="Species"; effectKind="type-resonance"; intentType=$dominantType; numericBonusPercent=10
        }
    }
    $speciesProfiles.Add([ordered]@{
        speciesId=$speciesId; profileId=$profileId; initialPassiveId=$passiveId; defaultIntentIds=@($defaults)
    })
}
$passives += @($speciesPassives.Values | Sort-Object id)

$document = [ordered]@{
    schemaVersion = 1
    commonIntents = $commonIntents
    passives = $passives
    speciesProfiles = @($speciesProfiles | Sort-Object profileId)
}
$document | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $outputPath -Encoding utf8NoBOM

if ($UpdateNativeIntentCosts) {
    foreach ($intent in $intentRegistry.intents) {
        if ([string]$intent.pool -ne "Pve") { continue }
        $effects = @($intent.effects)
        if ($effects.Count -eq 0) { $effects = @($intent) }
        $maxHits = ($effects | ForEach-Object { [Math]::Max(1, [int]$_.hitCount) } | Measure-Object -Maximum).Maximum
        $maxStacks = ($effects | ForEach-Object { [Math]::Max(0, [int]$_.buffStacks) } | Measure-Object -Maximum).Maximum
        $maxScale = ($effects | ForEach-Object {
            [Math]::Max([double]$_.attackScale, [Math]::Max([double]$_.armorScale, [double]$_.magicScale))
        } | Measure-Object -Maximum).Maximum
        $cost = if ($maxHits -ge 4 -or $maxStacks -ge 5 -or $maxScale -ge 1.2 -or $effects.Count -ge 3) { 3 }
                elseif ($maxHits -ge 2 -or $maxStacks -ge 2 -or $maxScale -ge 0.7 -or $effects.Count -ge 2) { 2 }
                else { 1 }
        $intent.cost = $cost
        $intent.cooldown = if ($cost -ge 3) { 2 } elseif ($cost -eq 2) { 1 } else { 0 }
    }
}
$intentRegistry | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $intentPath -Encoding utf8NoBOM

$designDirectory = Join-Path $repoRoot "docs/Terrias/design"
$abilityTablePath = Join-Path $designDirectory "09-精灵养成能力运行时表.csv"
$nativeTablePath = Join-Path $designDirectory "10-精灵固有意图魔能冷却表.csv"
$abilityRows = New-Object System.Collections.Generic.List[object]
foreach ($intent in $commonIntents) {
    $abilityRows.Add([pscustomobject]@{
        kind="Intent"; pool=$intent.pool; id=$intent.id; displayName=$intent.displayName
        type=$intent.type; cost=$intent.cost; cooldown=$intent.cooldown
        effectKind=$intent.handlerId; description=$intent.description
    })
}
foreach ($passive in $passives) {
    $abilityRows.Add([pscustomobject]@{
        kind="Passive"; pool=$passive.pool; id=$passive.id; displayName=$passive.displayName
        type=$passive.intentType; cost=""; cooldown=""
        effectKind=$passive.effectKind; description=$passive.description
    })
}
$abilityRows | Export-Csv -LiteralPath $abilityTablePath -NoTypeInformation -Encoding utf8NoBOM

$nativeRows = New-Object System.Collections.Generic.List[object]
foreach ($profile in $intentRegistry.profiles | Where-Object profileId -ne "") {
    $trainingProfile = $speciesProfiles | Where-Object profileId -eq $profile.profileId | Select-Object -First 1
    $defaults = @($trainingProfile.defaultIntentIds)
    $pveIds = @($profile.pveAttackTendency) + @($profile.pveDefenseTendency) | Select-Object -Unique
    foreach ($id in $pveIds) {
        if (-not $intentById.ContainsKey($id)) { continue }
        $intent = $intentById[$id]
        $nativeRows.Add([pscustomobject]@{
            profileId=$profile.profileId; enemyId=$profile.enemyId; intentId=$id
            enemyCardId=$intent.enemyCardId; displayName=$intent.displayName; type=$intent.type
            cost=$intent.cost; cooldown=$intent.cooldown; description=$intent.description
            defaultEquipped=($defaults -contains $id)
        })
    }
}
$nativeRows | Sort-Object profileId,intentId | Export-Csv -LiteralPath $nativeTablePath -NoTypeInformation -Encoding utf8NoBOM

Write-Host "Generated $outputPath"
Write-Host "commonIntents=$($commonIntents.Count) passives=$($passives.Count) speciesProfiles=$($speciesProfiles.Count)"
