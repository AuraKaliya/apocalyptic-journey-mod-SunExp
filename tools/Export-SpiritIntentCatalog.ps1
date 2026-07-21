param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\docs\Terrias\modules\10-游戏主体敌人与精灵专属意图总表.md')
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Get-Rows([string]$Path) {
    @(Import-Csv -LiteralPath $Path -Encoding UTF8 | Where-Object {
        -not [string]::IsNullOrWhiteSpace([string]$_.Id) -and
        [string]$_.Id -notin @('Id', '唯1标识', '唯一标识', '唯一的标识（不能重复）')
    })
}

function Get-CardKey([string]$Id) {
    $value = ([string]$Id).Trim().TrimStart('*')
    $value = $value -replace '(?i)^Terrias_terrias_enemycard_', ''
    $value = $value -replace '(?i)^enemycard_', ''
    $value.ToLowerInvariant()
}

function Get-BuffKey([string]$Id) {
    $value = ([string]$Id).Trim()
    $value = $value -replace '(?i)^Terrias_terrias_', ''
    $value = $value -replace '(?i)^buff_', ''
    $value.ToLowerInvariant()
}

function Escape-Cell([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return '—' }
    (($Value.Trim() -replace '\|', '\|') -replace "`r?`n", '<br>')
}

function Format-Number([double]$Value) {
    if ([Math]::Abs($Value - [Math]::Round($Value)) -lt 0.00001) {
        return ([int][Math]::Round($Value)).ToString()
    }
    $Value.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture)
}

function Get-ScriptNumber([string]$Script, [string]$Name, [int]$Default) {
    if (([string]$Script) -match ('Vars\["' + [regex]::Escape($Name) + '"\]\s*=\s*"(\d+)"')) {
        return [int]$Matches[1]
    }
    $Default
}

$baseEnemyDataPath = Join-Path $repoRoot 'apocalyptic-journey-mod-tutorial\ModTemplate\Scripts\Lib\DataConfigs\Data\Enemy\enemy.csv'
$baseEnemyTextPath = Join-Path $repoRoot 'apocalyptic-journey-mod-tutorial\ModTemplate\Scripts\Lib\DataConfigs\Text\Enemy\enemy.csv'
$baseCardDataPath = Join-Path $repoRoot 'apocalyptic-journey-mod-tutorial\ModTemplate\Scripts\Lib\DataConfigs\Data\EnemyCard\enemycard.csv'
$baseCardTextPath = Join-Path $repoRoot 'apocalyptic-journey-mod-tutorial\ModTemplate\Scripts\Lib\DataConfigs\Text\EnemyCard\enemycard.csv'
$baseBuffTextPath = Join-Path $repoRoot 'apocalyptic-journey-mod-tutorial\ModTemplate\Scripts\Lib\DataConfigs\Text\Buff\buff.csv'
$sunEnemyDataPath = Join-Path $repoRoot 'Terrias\Data\Enemy\terrias.csv'
$sunEnemyTextPath = Join-Path $repoRoot 'Terrias\Text\Enemy\terrias.csv'
$sunCardDataPath = Join-Path $repoRoot 'Terrias\Data\EnemyCard\terrias.csv'
$sunCardTextPath = Join-Path $repoRoot 'Terrias\Text\EnemyCard\terrias.csv'
$sunBuffTextPath = Join-Path $repoRoot 'Terrias\Text\Buff\terrias.csv'
$registryPath = Join-Path $repoRoot 'Terrias\spirit.intent.registry.json'

$baseEnemies = Get-Rows $baseEnemyDataPath
$sunEnemies = Get-Rows $sunEnemyDataPath
$enemyTexts = @((Get-Rows $baseEnemyTextPath) + (Get-Rows $sunEnemyTextPath))
$cardDataRows = @((Get-Rows $baseCardDataPath) + (Get-Rows $sunCardDataPath))
$cardTextRows = @((Get-Rows $baseCardTextPath) + (Get-Rows $sunCardTextPath))
$buffTextRows = @((Get-Rows $baseBuffTextPath) + (Get-Rows $sunBuffTextPath))
$registry = Get-Content -Raw -Encoding UTF8 $registryPath | ConvertFrom-Json

$enemyTextById = @{}
foreach ($row in $enemyTexts) { $enemyTextById[[string]$row.Id] = $row }
$cardDataByKey = @{}
foreach ($row in $cardDataRows) { $cardDataByKey[(Get-CardKey $row.Id)] = $row }
$cardTextByKey = @{}
foreach ($row in $cardTextRows) { $cardTextByKey[(Get-CardKey $row.Id)] = $row }
$buffNameByKey = @{}
foreach ($row in $buffTextRows) {
    if (-not [string]::IsNullOrWhiteSpace([string]$row.Name)) {
        $buffNameByKey[(Get-BuffKey $row.Id)] = [string]$row.Name
    }
}

# Terrias 文本表中的命名空间 Buff 在注册表中使用完整 DataId；这些名称也用于文档展示。
$buffNameByKey['gathered_flame'] = '聚焰'
$buffNameByKey['body_burn'] = '躯体燃烧'

$intentById = @{}
foreach ($intent in $registry.intents) { $intentById[[string]$intent.id] = $intent }
$profileByEnemyId = @{}
foreach ($profile in $registry.profiles) { $profileByEnemyId[[string]$profile.enemyId] = $profile }

function Get-BuffLabel([string]$Id) {
    $key = Get-BuffKey $Id
    $name = $buffNameByKey[$key]
    if ([string]::IsNullOrWhiteSpace([string]$name)) { return ('`' + $Id + '`') }
    "$name（``$Id``）"
}

function Get-TargetLabel($Intent) {
    switch ([string]$Intent.target.policy) {
        'enemy.lowest_hp' { '敌方生命值最低的存活单位' }
        'enemy.all' { '全部存活敌人' }
        'friendly.owner_or_self_defense' { '精灵拥有者；无法解析拥有者时回退精灵自身' }
        'friendly.all' { '全部真实友方角色' }
        'friendly.most_wounded' { '受伤比例最高的真实友方角色；全员满血时不可选' }
        'pvp.opponent' { '未来 PvP 的敌对玩家（当前 PvE 不可执行）' }
        default { "``$([string]$Intent.target.policy)``" }
    }
}

function Get-IntentTypeLabel([string]$Type) {
    switch ($Type) {
        'Attack' { '攻击' }
        'Defense' { '防御' }
        'Support' { '支援' }
        'Recovery' { '恢复' }
        'Interference' { '干扰' }
        default { $Type }
    }
}

function Get-IntentSuffix([string]$Handler) {
    switch -Wildcard ($Handler) {
        'damage.*' { '伤害' }
        'block.*' { '护盾' }
        'heal.*' { '治疗' }
        'buff.*' { '状态' }
        'pvp.*' { 'PvP 预留' }
        default { '适配' }
    }
}

function Get-IntentEffect($Intent) {
    $flat = Format-Number ([double]$Intent.flatValue)
    $attack = Format-Number ([double]$Intent.attackScale)
    $armor = Format-Number ([double]$Intent.armorScale)
    $magic = Format-Number ([double]$Intent.magicScale)
    $hits = [int]$Intent.hitCount
    switch ([string]$Intent.handlerId) {
        'damage.single' {
            if ([double]$Intent.attackScale -gt 0) { return "造成 $flat + 精灵攻击×$attack 点伤害" }
            return "造成 $flat 点伤害"
        }
        'damage.multi' {
            if ([double]$Intent.attackScale -gt 0) { return "造成 $hits 段伤害，每段为 $flat + 精灵攻击×$attack" }
            return "造成 $hits 段伤害，每段 $flat 点"
        }
        'damage.all' {
            if ([double]$Intent.attackScale -gt 0) { return "对全部目标造成 $flat + 精灵攻击×$attack 点伤害" }
            return "对全部目标造成 $flat 点伤害"
        }
        'block.single' {
            if ([double]$Intent.armorScale -gt 0) { return "增加 $flat + 精灵护甲×$armor 点护盾" }
            return "增加 $flat 点护盾"
        }
        'block.all' {
            if ([double]$Intent.armorScale -gt 0) { return "全体增加 $flat + 精灵护甲×$armor 点护盾" }
            return "全体增加 $flat 点护盾"
        }
        'heal.single' {
            if ([double]$Intent.magicScale -gt 0) { return "恢复 $flat + 精灵魔力×$magic 点生命" }
            return "恢复 $flat 点生命"
        }
        'buff.apply' { return "施加 $(Get-BuffLabel ([string]$Intent.buffId)) ×$([int]$Intent.buffStacks) 层" }
        'pvp.reserved' { return '仅登记来源、冷却与优先级；当前 PvE 不执行原卡组/塞牌/货币效果' }
        default { return "由白名单处理器 ``$([string]$Intent.handlerId)`` 执行" }
    }
}

function Get-IntentLine($Intent, [string]$SourceName) {
    if (@($Intent.effects).Count -gt 0) {
        $parts = [Collections.Generic.List[string]]::new()
        foreach ($effect in @($Intent.effects | Sort-Object { [int]$_.displayIndex })) {
            $label = "$SourceName·$(Get-IntentSuffix ([string]$effect.handlerId))"
            $type = switch -Wildcard ([string]$effect.handlerId) {
                'damage.*' { '攻击' }
                'block.*' { '防御' }
                'heal.*' { '恢复' }
                'buff.*' { if ([string]$effect.target.scope -eq 'Enemy') { '干扰' } else { '支援' } }
                default { Get-IntentTypeLabel ([string]$Intent.type) }
            }
            $placeholder = [int]$effect.displayIndex - 1
            $parts.Add("**$label**（$type）：$(Get-IntentEffect $effect)；目标：$(Get-TargetLabel $effect)；描述占位符：{$placeholder}")
        }
        return (($parts -join '<br><br>') + "<br><br>复合专属意图 ID：``$([string]$Intent.id)``")
    }

    $label = "$SourceName·$(Get-IntentSuffix ([string]$Intent.handlerId))"
    $type = Get-IntentTypeLabel ([string]$Intent.type)
    $effect = Get-IntentEffect $Intent
    $target = Get-TargetLabel $Intent
    "**$label**（$type）：$effect；目标：$target；专属意图 ID：``$([string]$Intent.id)``"
}

function Get-EnemyName($Enemy) {
    $text = $enemyTextById[[string]$Enemy.Id]
    if ($null -ne $text -and -not [string]::IsNullOrWhiteSpace([string]$text.Name)) { return [string]$text.Name }
    [string]$Enemy.Name
}

function Get-CardName([string]$SourceId) {
    $text = $cardTextByKey[(Get-CardKey $SourceId)]
    if ($null -ne $text -and -not [string]::IsNullOrWhiteSpace([string]$text.Name)) { return [string]$text.Name }
    '中文名缺失'
}

function Get-CardDescription([string]$SourceId) {
    $text = $cardTextByKey[(Get-CardKey $SourceId)]
    if ($null -ne $text) { return [string]$text.Description }
    ''
}

function Get-ProfileIntentIds($Profile, [string]$PropertyName) {
    if ($null -eq $Profile) { return @() }
    @($Profile.$PropertyName | ForEach-Object { [string]$_ })
}

function Get-SourceIntentIds($Profile, [string]$SourceId, [string[]]$PoolProperties) {
    $sourceKey = Get-CardKey $SourceId
    $ids = foreach ($property in $PoolProperties) {
        foreach ($id in (Get-ProfileIntentIds $Profile $property)) {
            $intent = $intentById[$id]
            if ($null -ne $intent -and (Get-CardKey ([string]$intent.enemyCardId)) -eq $sourceKey) { $id }
        }
    }
    @($ids | Select-Object -Unique)
}

function Test-SourceList($Profile, [string]$PropertyName, [string]$SourceId) {
    if ($null -eq $Profile) { return $false }
    $key = Get-CardKey $SourceId
    @($Profile.$PropertyName | Where-Object { (Get-CardKey ([string]$_)) -eq $key }).Count -gt 0
}

function Assert-CatalogCoverage([object[]]$Enemies) {
    $errors = [Collections.Generic.List[string]]::new()
    foreach ($enemy in $Enemies) {
        $enemyId = ([string]$enemy.Id).Trim().TrimStart('*')
        $profile = $profileByEnemyId[$enemyId]
        if ($null -eq $profile) {
            $errors.Add("敌人 $enemyId 缺少精灵 profile。")
            continue
        }
        $localizedEnemyName = if ($null -ne $enemyTextById[$enemyId]) { [string]$enemyTextById[$enemyId].Name } else { '' }
        if ([string]::IsNullOrWhiteSpace($localizedEnemyName) -and [string]::IsNullOrWhiteSpace([string]$enemy.Name)) {
            $errors.Add("敌人 $enemyId 缺少中文名称。")
        }

        $cards = @(([string]$enemy.CardList).Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        foreach ($sourceId in $cards) {
            $key = Get-CardKey $sourceId
            if ($null -eq $cardDataByKey[$key]) { $errors.Add("敌人 $enemyId 的原卡 $sourceId 缺少 Data/EnemyCard 数据。") }
            if ($null -eq $cardTextByKey[$key] -or [string]::IsNullOrWhiteSpace([string]$cardTextByKey[$key].Name)) {
                $errors.Add("敌人 $enemyId 的原卡 $sourceId 缺少中文名称。")
            }
            if (-not (Test-SourceList $profile 'sourceEnemyCardIds' $sourceId)) {
                $errors.Add("敌人 $enemyId 的 profile 未保存原卡 $sourceId。")
            }

            $pve = Get-SourceIntentIds $profile $sourceId @('pveAttackTendency', 'pveDefenseTendency')
            $pvp = Get-SourceIntentIds $profile $sourceId @('pvpAttackTendency', 'pvpDefenseTendency')
            $fallback = Test-SourceList $profile 'fallbackSourceEnemyCardIds' $sourceId
            if ($pve.Count -eq 0 -and $pvp.Count -eq 0 -and -not $fallback) {
                $errors.Add("敌人 $enemyId 的原卡 $sourceId 未进入 PvE、PvP 或后备分类。")
            }
        }
    }

    if ($errors.Count -gt 0) {
        throw ("精灵意图目录覆盖检查失败：`n- " + ($errors -join "`n- "))
    }
}

function Add-EnemySection([Collections.Generic.List[string]]$Lines, $Enemy, [string]$ScopeLabel) {
    $enemyId = ([string]$Enemy.Id).Trim().TrimStart('*')
    $enemyName = Get-EnemyName $Enemy
    $profile = $profileByEnemyId[$enemyId]
    $cards = @(([string]$Enemy.CardList).Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $attackWeight = if ($null -ne $profile) { [int]$profile.attackWeight } else { 0 }
    $defenseWeight = if ($null -ne $profile) { [int]$profile.defenseWeight } else { 0 }

    $Lines.Add("### $enemyName（``$enemyId``）")
    $Lines.Add('')
    $Lines.Add("- 范围：$ScopeLabel；原始强度/稀有度：$([string]$Enemy.Rarity)；原始行动次数：$([string]$Enemy.ActionCount)。")
    $Lines.Add("- 原始意图卡：$($cards.Count) 张；捕获后攻/防倾向权重：$attackWeight / $defenseWeight。")
    $Lines.Add('')
    $Lines.Add('| 原始意图（中文名 / ID） | 原始卡牌描述 | CD / 基础优先级 | 捕获后的 PvE 专属意图 | PvP 预留 / 后备处理 |')
    $Lines.Add('| --- | --- | ---: | --- | --- |')

    foreach ($sourceId in $cards) {
        $cardName = Get-CardName $sourceId
        $description = Escape-Cell (Get-CardDescription $sourceId)
        $data = $cardDataByKey[(Get-CardKey $sourceId)]
        $cooldown = if ($null -ne $data) { Get-ScriptNumber ([string]$data.InitScript) 'CD' 0 } else { 0 }
        $priority = if ($null -ne $data) { Get-ScriptNumber ([string]$data.InitScript) 'priority' 1 } else { 1 }
        $pveIds = Get-SourceIntentIds $profile $sourceId @('pveAttackTendency', 'pveDefenseTendency')
        $pvpIds = Get-SourceIntentIds $profile $sourceId @('pvpAttackTendency', 'pvpDefenseTendency')

        $pve = if ($pveIds.Count -gt 0) {
            (($pveIds | ForEach-Object { Get-IntentLine $intentById[$_] $cardName }) -join '<br><br>')
        } else {
            '未生成该原卡的 PvE 专属效果。'
        }

        $notes = [Collections.Generic.List[string]]::new()
        if ($pvpIds.Count -gt 0) {
            foreach ($id in $pvpIds) { $notes.Add((Get-IntentLine $intentById[$id] $cardName)) }
        }
        if (Test-SourceList $profile 'fallbackSourceEnemyCardIds' $sourceId) {
            $notes.Add('该来源登记为后备来源，不直接执行原脚本。只有对应的整个专属攻/防倾向池为空时，才分别回退到“法杖敲头”或“魔能护盾”。')
        }
        if ($notes.Count -eq 0) { $notes.Add('无。') }

        $original = "**$(Escape-Cell $cardName)**<br>``$(Escape-Cell $sourceId)``"
        $Lines.Add("| $original | $description | $cooldown / $priority | $(Escape-Cell $pve) | $(Escape-Cell ($notes -join '<br><br>')) |")
    }
    $Lines.Add('')
}

$baseSourceIds = @($baseEnemies | ForEach-Object { ([string]$_.CardList).Split(',') } | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Sort-Object -Unique)
$sunSourceIds = @($sunEnemies | ForEach-Object { ([string]$_.CardList).Split(',') } | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Sort-Object -Unique)
$allSpecificProfiles = @($registry.profiles | Where-Object { $_.enemyId -ne '*' })
$pveIntents = @($registry.intents | Where-Object { $_.pool -eq 'Pve' })
$pvpIntents = @($registry.intents | Where-Object { $_.pool -eq 'PvpReserved' })
$nativeBlockProfiles = @($baseEnemies | Where-Object {
    $profile = $profileByEnemyId[([string]$_.Id).TrimStart('*')]
    @(@($profile.pveDefenseTendency) | Where-Object {
        $resolved = $intentById[[string]$_]
        $resolved.handlerId -like 'block.*' -or @($resolved.effects | Where-Object handlerId -like 'block.*').Count -gt 0
    }).Count -gt 0
})
$blockIntents = @($pveIntents | Where-Object { $_.handlerId -like 'block.*' -or @($_.effects | Where-Object handlerId -like 'block.*').Count -gt 0 })
$zeroCdBlockRows = @($blockIntents | Where-Object { [int]$_.cooldown -eq 0 })

Assert-CatalogCoverage @($baseEnemies + $sunEnemies)

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# 游戏主体敌人与精灵专属意图总表')
$lines.Add('')
$lines.Add('> 数据基线：2026-07-13  ')
$lines.Add('> 游戏主体参考：`apocalyptic-journey-mod-tutorial/ModTemplate/Scripts/Lib/DataConfigs`  ')
$lines.Add('> 精灵运行配置：`Terrias/spirit.intent.registry.json`（schema 3）  ')
$lines.Add('> 生成工具：`tools/Export-SpiritIntentCatalog.ps1`')
$lines.Add('')
$lines.Add('## 1. 文档范围与读法')
$lines.Add('')
$lines.Add('本文以当前仓库内的游戏主体 Enemy/EnemyCard 数据和 Terrias 已发布精灵注册表为唯一事实来源，逐个列出游戏主体全部敌人的原始意图，并给出捕获后实际进入该精灵 profile 的专属意图。Terrias 自有的三个日耀 BOSS 单列于附录，不混入游戏主体数量。')
$lines.Add('')
$lines.Add('需要特别区分三种“名称”：')
$lines.Add('')
$lines.Add('- “原始意图中文名”来自游戏主体 `Text/EnemyCard/enemycard.csv`。')
$lines.Add('- 精灵行动展示仍复制原敌人卡 DataConfig，因此实机卡面继续显示原始中文名、图标和动作。')
$lines.Add('- 文档中的“原名·伤害 / 护盾 / 状态”等后缀用于区分同一个复合意图中的多段白名单效果，不代表运行时把卡面重命名。')
$lines.Add('')
$lines.Add('原始描述记录游戏主体敌人使用该卡时的语义；“捕获后的 PvE 专属意图”记录精灵实际执行的安全适配语义。两者不相同时，以后者作为精灵当前行为。原敌人脚本不会由精灵直接执行。')
$lines.Add('')
$lines.Add('## 2. 覆盖汇总')
$lines.Add('')
$lines.Add('| 项目 | 数量 |')
$lines.Add('| --- | ---: |')
$lines.Add("| 游戏主体敌人 | $($baseEnemies.Count) |")
$lines.Add("| 游戏主体敌人—意图卡归属关系（同卡被多个敌人持有时重复计数） | $(@($baseEnemies | ForEach-Object { @(([string]$_.CardList).Split(',') | Where-Object { $_.Trim() }) }).Count) |")
$lines.Add("| 游戏主体被引用的不同原始意图卡 | $($baseSourceIds.Count) |")
$lines.Add("| Terrias 自有可捕获敌人 / BOSS | $($sunEnemies.Count) |")
$lines.Add("| Terrias 自有不同原始意图卡 | $($sunSourceIds.Count) |")
$lines.Add("| 显式精灵 profile（主体 + Terrias） | $($allSpecificProfiles.Count) |")
$lines.Add("| 已发布 PvE 专属意图定义 | $($pveIntents.Count) |")
$lines.Add("| 已发布 PvP 预留意图定义 | $($pvpIntents.Count) |")
$lines.Add('')
$lines.Add('当前 56 个游戏主体敌人均有明确 profile 和中文敌人名；其引用的 62 种原始意图卡均能在游戏主体中文文本表中找到名称与描述。')
$lines.Add('')
$lines.Add('## 3. 捕获后意图的通用规则')
$lines.Add('')
$lines.Add('| 类型 | 当前精灵适配 |')
$lines.Add('| --- | --- |')
$lines.Add('| 普通单体伤害 | `2 + 精灵攻击×0.8`，目标为生命值最低的存活敌人 |')
$lines.Add('| 普通多段伤害 | 保留识别出的段数；每段 `2 + 精灵攻击×0.35` |')
$lines.Add('| 普通护盾 | `4 + 精灵护甲×0.8`，重定向给精灵拥有者 |')
$lines.Add('| 原生自我治疗 | `4 + 精灵魔力×0.6`，改为治疗受伤比例最高的真实友方 |')
$lines.Add('| Buff / Debuff | 保留生成器识别并登记的全部安全 Buff；按敌我语义重定向目标 |')
$lines.Add('| 塞牌、改卡组、货币交互 | 只进入 `PvpReserved`，当前 PvE 不执行 |')
$lines.Add('| 召敌、唤醒阶段、复制全场状态等 | 登记为后备来源，不执行原脚本 |')
$lines.Add('')
$lines.Add('CD 与基础优先级来自原卡 `InitScript`。意图选择仍会叠加动态类型优先级；表内“基础优先级”不是最终抽取权重。攻/防倾向先抽取，再在对应倾向内部按最终优先级加权选择。')
$lines.Add('')
$lines.Add('## 4. 与“护盾出现过多”直接相关的目录事实')
$lines.Add('')
$lines.Add("- 游戏主体 $($baseEnemies.Count) 个敌人中，$($nativeBlockProfiles.Count) 个捕获 profile 含至少一个原生护盾适配意图。")
$lines.Add("- 全注册表共有 $($blockIntents.Count) 个含护盾效果的复合意图，其中 $($zeroCdBlockRows.Count) 个为 CD 0；这些定义会被大量敌人 profile 复用。")
$lines.Add('- 最常见来源是 `enemycard_defence`（中文名“魔力屏障”）：基础优先级 1、CD 0。只要拥有者存活，它的目标策略始终有效，即使当前护盾已经很高也不会从候选池移除。')
$lines.Add('- 因此，实机“经常护盾”主要来自目录覆盖率、CD 0 可连续使用和护盾目标永远有效，而不是单纯由基础优先级 1 导致。')
$lines.Add('')
$lines.Add('## 5. 游戏主体敌人完整对照')
$lines.Add('')

foreach ($enemy in $baseEnemies) { Add-EnemySection $lines $enemy '游戏主体' }

$lines.Add('## 6. 附录：Terrias 自有可捕获敌人 / BOSS')
$lines.Add('')
$lines.Add('以下对象不是游戏主体 56 个敌人的一部分，但已经进入同一精灵捕获与专属意图注册表，因此一并列出以保证发布配置可完整审计。')
$lines.Add('')
foreach ($enemy in $sunEnemies) { Add-EnemySection $lines $enemy 'Terrias 自有内容' }

$lines.Add('## 7. 后备与 PvP 语义说明')
$lines.Add('')
$lines.Add('- `PvpReserved` 仅表示数据已经分类，并不表示当前可在 PvE 或 PvP 中执行。处理器 `pvp.reserved` 当前不会产生效果。')
$lines.Add('- 某张原卡被登记为后备来源，不等于每次抽到该原卡都会自动改成通用护盾。精灵选择器读取的是整个 profile 的专属攻/防池；只有所请求倾向的专属池本身为空时，才使用后备攻击 `staff_tap` 或后备防御 `shield_blessing`。')
$lines.Add('- 一张原卡在 PvE 中只对应一个复合意图；伤害、护盾、治疗及多个安全 Buff 会作为该意图的多段效果按顺序执行。塞牌或货币副作用仍单独留在 PvP 预留分类。')
$lines.Add('')
$lines.Add('## 8. 数据维护与复核命令')
$lines.Add('')
$lines.Add('重新生成精灵注册表后，执行：')
$lines.Add('')
$lines.Add('```powershell')
$lines.Add('powershell -ExecutionPolicy Bypass -File tools/Export-SpiritIntentCatalog.ps1')
$lines.Add('powershell -ExecutionPolicy Bypass -File tools/Test-SpiritCapture.ps1')
$lines.Add('```')
$lines.Add('')
$lines.Add('本文是生成型目录，不应手工维护逐敌人表格。原敌人 CSV、中文文本、Terrias BOSS 数据或 `spirit.intent.registry.json` 变化后，应运行生成工具整体刷新。')

$outputFullPath = if ([IO.Path]::IsPathRooted($OutputPath)) {
    [IO.Path]::GetFullPath($OutputPath)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputPath))
}
$outputDirectory = Split-Path -Parent $outputFullPath
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
[IO.File]::WriteAllText($outputFullPath, (($lines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))

Write-Host "Wrote spirit intent catalog: $outputFullPath"
Write-Host "Base enemies=$($baseEnemies.Count), base source cards=$($baseSourceIds.Count), PvE intents=$($pveIntents.Count), PvP reserved=$($pvpIntents.Count)."
