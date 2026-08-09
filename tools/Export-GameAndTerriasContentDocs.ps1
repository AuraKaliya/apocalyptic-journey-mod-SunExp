param(
    [string]$TableExport = "",
    [string]$RepoRoot = "",
    [switch]$BaseGameOnly
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
}
else {
    $RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
}

if ([string]::IsNullOrWhiteSpace($TableExport)) {
    $exportRoot = Join-Path $RepoRoot "docs\游戏主体内容\combat-knowledge\table-exports"
    $latestExport = Get-ChildItem -LiteralPath $exportRoot -Filter "witch-tables-*.json" |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $latestExport) {
        throw "No witch-tables export was found under $exportRoot."
    }

    $TableExport = $latestExport.FullName
}
elseif (-not [System.IO.Path]::IsPathRooted($TableExport)) {
    $TableExport = Join-Path $RepoRoot $TableExport
}

$catalog = Get-Content -LiteralPath $TableExport -Raw -Encoding UTF8 | ConvertFrom-Json
$tables = $catalog.Tables
$gameBuild = ([string]$catalog.GameBuild).TrimStart("v")
$exportName = Split-Path -Leaf $TableExport
$exportDate = ([DateTime]$catalog.ExportedAtUtc).ToString("yyyy-MM-dd")
$exportSource = if ([string]$catalog.ExportSource -eq "installed-addressables+previous-runtime-derived-keywords") {
    "安装目录 Addressables 表重建（派生关键词沿用上一份运行时导出模板）"
}
else {
    "运行时表导出"
}
$modConfig = Get-Content -LiteralPath (Join-Path $RepoRoot "Terrias\ModConfig.json") -Raw -Encoding UTF8 | ConvertFrom-Json

function Format-MarkdownText {
    param(
        [AllowEmptyString()]
        [string]$Text,
        [string]$Fallback = "无"
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $Fallback
    }

    $value = $Text.Trim()
    $value = $value -replace "\r?\n", "<br>"
    $value = $value -replace "\|", "／"
    return $value
}

function Format-Code {
    param([AllowEmptyString()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return "无"
    }

    return "``$Text``"
}

function Get-TaggedPart {
    param(
        [AllowEmptyString()]
        [string]$Text,
        [Parameter(Mandatory)]
        [string]$Tag
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    $match = [regex]::Match($Text, "(?s)<$Tag>(.*?)</$Tag>")
    if (-not $match.Success) {
        return ""
    }

    return $match.Groups[1].Value.Trim()
}

function Get-ActionRecord {
    param(
        [AllowEmptyString()]
        [string]$Text,
        [AllowEmptyString()]
        [string]$CardId
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    return [pscustomobject]@{
        Name = Get-TaggedPart -Text $Text -Tag "name"
        Description = Get-TaggedPart -Text $Text -Tag "des"
        Cooldown = (Get-TaggedPart -Text $Text -Tag "cd") -replace "^\s*CD\s*:\s*", ""
        CardId = $CardId
    }
}

function Get-PassiveRecord {
    param([AllowEmptyString()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    return [pscustomobject]@{
        Name = Get-TaggedPart -Text $Text -Tag "name"
        Description = Get-TaggedPart -Text $Text -Tag "des"
    }
}

function Add-CharacterDetails {
    param(
        [Parameter(Mandatory)]
        $Lines,
        [Parameter(Mandatory)]
        [object]$Career,
        [int]$HeadingLevel = 3
    )

    $heading = "#" * $HeadingLevel
    $Lines.Add("$heading $($Career.Name) · $($Career.Title)（``$($Career.Id)``）")
    $Lines.Add("")
    $Lines.Add("- SAN 上限：$($Career.SanMax)。")

    $actions = @(
        Get-ActionRecord -Text $Career.Action1 -CardId $Career.Skill1
        Get-ActionRecord -Text $Career.Action2 -CardId $Career.Skill2
    ) | Where-Object { $null -ne $_ }

    if ($actions.Count -gt 0) {
        $Lines.Add("")
        $Lines.Add("| 主动技能 | 对应技能牌 | 冷却 | 效果 |")
        $Lines.Add("|---|---|---:|---|")
        foreach ($action in $actions) {
            $cooldown = if ([string]::IsNullOrWhiteSpace($action.Cooldown)) { "未标注" } else { $action.Cooldown }
            $Lines.Add("| $(Format-MarkdownText $action.Name) | $(Format-Code $action.CardId) | $cooldown | $(Format-MarkdownText $action.Description) |")
        }
    }

    $passives = @(
        Get-PassiveRecord -Text $Career.Passive1
        Get-PassiveRecord -Text $Career.Passive2
    ) | Where-Object { $null -ne $_ }

    if ($passives.Count -gt 0) {
        $Lines.Add("")
        $Lines.Add("| 被动 | 效果 |")
        $Lines.Add("|---|---|")
        foreach ($passive in $passives) {
            $Lines.Add("| $(Format-MarkdownText $passive.Name) | $(Format-MarkdownText $passive.Description) |")
        }
    }

    $Lines.Add("")
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        $Lines
    )

    $directory = Split-Path -Parent $Path
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    # Keep generated Markdown stable across PowerShell hosts and Git autocrlf settings.
    $content = ($Lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText($Path, $content, [System.Text.UTF8Encoding]::new($false))
}

# Game body: playable careers and their native familiar blessings.
$baseCareers = @($tables.Career | Where-Object { $_.Id -match "^career_\d+$" })
$selectableCareers = @($baseCareers | Where-Object { $_.Id -ne "career_4" } | Sort-Object { [int]($_.Id -replace "\D", "") })
$transformationCareers = @($baseCareers | Where-Object { $_.Id -eq "career_4" })
$basePartners = @($tables.Partner | Where-Object { $_.Id -match "^Partner_\d+$" } | Sort-Object Id)
$blessById = @{}
foreach ($bless in $tables.Bless) {
    $blessById[$bless.Id] = $bless
}

$gameLines = [System.Collections.Generic.List[string]]::new()
$gameLines.Add("# 游戏主体角色技能与使魔祝福总表")
$gameLines.Add("")
$gameLines.Add("- 口径：游戏构建 ``$gameBuild``；源数据为$exportSource ``$exportName``（$exportDate）。")
$gameLines.Add("- 主体与 MOD 通过完整运行时 ID 分离：角色仅收录 ``career_*``，使魔仅收录 ``Partner_*``，不包含 ``Terrias_*`` 等扩展内容。")
$gameLines.Add("- 共归纳 $($selectableCareers.Count) 名可选角色、$($transformationCareers.Count) 个战斗形态与 $($basePartners.Count) 只主体使魔。角色技能说明保留当前游戏显示口径；动态计算仍以实战脚本为准。")
$gameLines.Add("- ``career_4`` 是奈奈的【灾厄化身】，不是独立可选角色；技能牌 ID 中的 ``*`` 表示内部技能牌，不代表该角色技能不可用。")
$gameLines.Add("")
$gameLines.Add("## 角色速览")
$gameLines.Add("")
$gameLines.Add("| 角色 | 称号 | 运行时 ID | SAN 上限 | 主动技能 | 核心被动 |")
$gameLines.Add("|---|---|---|---:|---|---|")
foreach ($career in $selectableCareers) {
    $actionNames = @(
        Get-TaggedPart -Text $career.Action1 -Tag "name"
        Get-TaggedPart -Text $career.Action2 -Tag "name"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $passiveNames = @(
        Get-TaggedPart -Text $career.Passive1 -Tag "name"
        Get-TaggedPart -Text $career.Passive2 -Tag "name"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $gameLines.Add("| $($career.Name) | $($career.Title) | ``$($career.Id)`` | $($career.SanMax) | $(Format-MarkdownText ($actionNames -join "、")) | $(Format-MarkdownText ($passiveNames -join "、")) |")
}

$gameLines.Add("")
$gameLines.Add("## 角色技能明细")
$gameLines.Add("")
foreach ($career in $selectableCareers) {
    Add-CharacterDetails -Lines $gameLines -Career $career
}

$gameLines.Add("## 战斗形态")
$gameLines.Add("")
foreach ($career in $transformationCareers) {
    Add-CharacterDetails -Lines $gameLines -Career $career
}

$gameLines.Add("## 使魔祝福")
$gameLines.Add("")
$gameLines.Add("| 使魔 | 使魔 ID | 祝福 | 祝福 ID | 实际效果 |")
$gameLines.Add("|---|---|---|---|---|")
foreach ($partner in $basePartners) {
    $bless = $blessById[$partner.Bless]
    $blessName = if ($null -ne $bless) { $bless.Name } else { Get-TaggedPart -Text $partner.Passive1 -Tag "name" }
    $description = if ($null -ne $bless) { $bless.Description } else { Get-TaggedPart -Text $partner.Passive1 -Tag "des" }
    $gameLines.Add("| $($partner.Name) | ``$($partner.Id)`` | $(Format-MarkdownText $blessName) | ``$($partner.Bless)`` | $(Format-MarkdownText $description) |")
}
$gameLines.Add("")
$gameLines.Add("## 构筑定位")
$gameLines.Add("")
$gameLines.Add("| 内容 | 更适合的方向 | 使用提醒 |")
$gameLines.Add("|---|---|---|")
$gameLines.Add("| 阿米莉娅 | 稳定检索、坚毅、元素与超凡 | 技能冷却短，适合作为泛用构筑基底。 |")
$gameLines.Add("| 奈奈／灾厄化身 | 负面状态吞噬、厄运魔能、形态切换 | SAN 上限较低，需把吞噬与变身节奏一起规划。 |")
$gameLines.Add("| 阿黛拉 | 焚毁、灵魂、黑耀棋子 | 灵魂既决定长期成长，也决定回合开始生成的棋子质量。 |")
$gameLines.Add("| 卡洛琳 | 护盾、反击、意图破坏 | 伤害随护盾与反击层数成长，偏防守转输出。 |")
$gameLines.Add("| 厄米娅 | 混沌标记、随机波动、高生命 | 上下限差距大，技能用于重掷而非消除随机性。 |")
$gameLines.Add("| 可可 | Buff 摹写、湮灭、漫画祝福 | 目标选择与被湮灭卡的消耗／等阶决定收益。 |")
$gameLines.Add("| 薇薇安 | 流血、鬼化、全场层数爆发 | 适合让多单位共同堆叠流血后集中结算。 |")
$gameLines.Add("| 失心躯壳 | 临时复制、永久强化单卡、多次生效 | 【深渊鸣唤】冷却极长，适合围绕关键单卡建立冒险级成长。 |")
$gameLines.Add("| 五只主体使魔 | 全场追伤、复活、负面增幅、开局牌或随机行动牌 | 使魔祝福是随使魔绑定的战斗被动，不等同于普通祝福池中的自由选择。 |")

$gameOutput = Join-Path $RepoRoot "docs\游戏主体内容\角色技能与使魔祝福\游戏主体角色技能与使魔祝福总表.md"
Write-Utf8NoBom -Path $gameOutput -Lines $gameLines

# Terrias: player-facing content catalog from the same merged runtime snapshot.
if (-not $BaseGameOnly) {
$terriasPrefix = "Terrias_"
$terriasCards = @($tables.Card | Where-Object { $_.Id -like "$terriasPrefix*" } | Sort-Object Id)
$terriasPacks = @($tables.CardPack | Where-Object { $_.Id -like "$terriasPrefix*" })
$terriasCareers = @($tables.Career | Where-Object { $_.Id -like "$terriasPrefix*" } | Sort-Object Id)
$terriasBuffs = @($tables.Buff | Where-Object { $_.Id -like "$terriasPrefix*" })
$terriasRelics = @($tables.Relic | Where-Object { $_.Id -like "$terriasPrefix*" } | Sort-Object Rarity, Id)
$terriasBlesses = @($tables.Bless | Where-Object { $_.Id -like "$terriasPrefix*" } | Sort-Object Type, Id)
$terriasPartners = @($tables.Partner | Where-Object { $_.Id -like "$terriasPrefix*" } | Sort-Object Id)
$terriasHard = @($tables.Hard | Where-Object { $_.Id -like "$terriasPrefix*" } | Sort-Object Id)
$terriasEnemies = @($tables.Enemy | Where-Object { $_.Id -like "$terriasPrefix*" } | Sort-Object Level, Id)
$terriasEnemyCards = @($tables.EnemyCard | Where-Object { $_.Id -like "$terriasPrefix*" })
$terriasEnchTags = @($tables.EnchTag | Where-Object { $_.Id -like "$terriasPrefix*" } | Sort-Object Id)

$packById = @{}
foreach ($pack in $terriasPacks) {
    $packById[$pack.Id] = $pack
}

$packOrder = @(
    "Terrias_terrias_cardpack_solar_ember_crown_canopy",
    "Terrias_terrias_cardpack_morning_star_overture",
    "Terrias_terrias_cardpack_more_dimensions"
)
$packDescriptionOverrides = @{
    "Terrias_terrias_cardpack_more_dimensions" = "异次元机制卡包。提供百变、投影、心变、精灵球与特殊奖励牌入口。"
}

$terriasLines = [System.Collections.Generic.List[string]]::new()
$terriasLines.Add("# Terrias 扩展内容总览")
$terriasLines.Add("")
$terriasLines.Add("- 版本：Terrias ``$($modConfig.ModVersion)``；游戏构建 ``$gameBuild``；运行时表导出 ``$exportName``（$exportDate）。")
$terriasLines.Add("- 口径：仅统计完整运行时 ID 以 ``Terrias_`` 开头的已加载内容；技术实现、Hook 和联机协议另见 [Terrias 技术文档](../Terrias/README.md)。")
$terriasLines.Add("- 卡牌说明、Buff 公式和角色技能均保留当前中文显示文本。带有动态占位符的数值由实战状态或初始化脚本计算。")
$terriasLines.Add("- 本页将【公开卡包内容】和【角色技能牌／系统模板／模式专用牌】分开，后者不应默认视为普通奖励池内容。")
$terriasLines.Add("")
$terriasLines.Add("## 内容规模")
$terriasLines.Add("")
$terriasLines.Add("| 类型 | 数量 | 玩家侧定位 |")
$terriasLines.Add("|---|---:|---|")
$terriasLines.Add("| 角色 | $($terriasCareers.Count) | 乌娜、洛奈尔、哥伦比娅 |")
$terriasLines.Add("| 使魔 | $($terriasPartners.Count) | 黄昏、星泥人傀、桑多涅喵 |")
$terriasLines.Add("| 卡包 | $($terriasPacks.Count) | 1 个日耀、1 个晨星、1 个异次元卡包 |")
$terriasLines.Add("| 卡牌 | $($terriasCards.Count) | $(@($terriasCards | Where-Object { -not [string]::IsNullOrWhiteSpace($_.PackBelong) }).Count) 张卡包归属牌；$(@($terriasCards | Where-Object { [string]::IsNullOrWhiteSpace($_.PackBelong) }).Count) 张角色／系统／模式牌 |")
$terriasLines.Add("| Buff | $($terriasBuffs.Count) | 正面、负面、能力、契印、特性与场地 |")
$terriasLines.Add("| 遗物 | $($terriasRelics.Count) | 日耀合并卡包配套遗物 |")
$terriasLines.Add("| 祝福 | $($terriasBlesses.Count) | 3 个伙伴占位、4 个本源升华与 4 个日耀祝福 |")
$terriasLines.Add("| 火漆 | $($terriasEnchTags.Count) | 白曜、阳炣、启明星 |")
$terriasLines.Add("| 难度词条 | $($terriasHard.Count) | Terrias 与异次元主题规则 |")
$terriasLines.Add("| 专属敌人／意图 | $($terriasEnemies.Count)／$($terriasEnemyCards.Count) | 日耀回忆固定 Boss 与专属出招 |")
$terriasLines.Add("")
$terriasLines.Add("## 核心玩法")
$terriasLines.Add("")
$terriasLines.Add("| 体系 | 核心资源 | 主要循环 |")
$terriasLines.Add("|---|---|---|")
$terriasLines.Add("| 日耀 | 日耀、聚炎、余烬、烬衣、圣冕、炽灼天幕 | 施加或触发灼烧，转化聚炎与余烬，再以圣冕和场地完成爆发。 |")
$terriasLines.Add("| 晨星 | 星谱、伏谱、谱句、连音、启明星、星石袋 | 控制牌序与费用，完成【启承转合】谱句并复奏，借白石／黑石管理奇迹时钟。 |")
$terriasLines.Add("| 月之少女 | 重力涟漪、月之领域、月感电／月绽放／月结晶 | 以获得卡牌压缩技能冷却，在月之领域中强化月系联动。 |")
$terriasLines.Add("| 更多次元 | 百变、投影、心变、精灵球 | 复制角色、召唤投影、控制敌人与捕获精灵，扩展战斗单位与身份玩法。 |")
$terriasLines.Add("| 无尽之渊 | 注视、深渊震荡、裂隙、绝灭、进化 | 每层或每战承担代价换取成长；第 7 层起进入无尽阶段。 |")
$terriasLines.Add("")
$terriasLines.Add("## 角色技能")
$terriasLines.Add("")
foreach ($career in $terriasCareers) {
    Add-CharacterDetails -Lines $terriasLines -Career $career
}

$terriasLines.Add("## 使魔与绑定祝福")
$terriasLines.Add("")
$terriasLines.Add("| 使魔 | 使魔 ID | 绑定祝福 | 实际效果 |")
$terriasLines.Add("|---|---|---|---|")
foreach ($partner in $terriasPartners) {
    $bless = $blessById[$partner.Bless]
    $blessName = if ($null -ne $bless) { $bless.Name } else { Get-TaggedPart -Text $partner.Passive1 -Tag "name" }
    $description = if ($null -ne $bless) { $bless.Description } else { Get-TaggedPart -Text $partner.Passive1 -Tag "des" }
    $terriasLines.Add("| $($partner.Name) | ``$($partner.Id)`` | $(Format-MarkdownText $blessName)（``$($partner.Bless)``） | $(Format-MarkdownText $description) |")
}

$terriasLines.Add("")
$terriasLines.Add("## 三个卡包")
$terriasLines.Add("")
$terriasLines.Add("| 卡包 | 运行时 ID | 卡牌数 | 定位 |")
$terriasLines.Add("|---|---|---:|---|")
foreach ($packId in $packOrder) {
    $pack = $packById[$packId]
    if ($null -eq $pack) {
        continue
    }
    $count = @($terriasCards | Where-Object { $_.PackBelong -eq $packId }).Count
    $packDescription = if ($packDescriptionOverrides.ContainsKey($packId)) {
        $packDescriptionOverrides[$packId]
    }
    else {
        $pack.Description
    }
    $terriasLines.Add("| $($pack.Name) | ``$packId`` | $count | $(Format-MarkdownText $packDescription) |")
}

foreach ($packId in $packOrder) {
    $pack = $packById[$packId]
    if ($null -eq $pack) {
        continue
    }
    $cards = @($terriasCards | Where-Object { $_.PackBelong -eq $packId } | Sort-Object Rarity, Id)
    $terriasLines.Add("")
    $terriasLines.Add("### $($pack.Name)（$($cards.Count) 张）")
    $terriasLines.Add("")
    $terriasLines.Add("| 卡牌 ID | 名称 | 类型 | 稀有度 | 费用 | 效果 |")
    $terriasLines.Add("|---|---|---|---:|---:|---|")
    foreach ($card in $cards) {
        $terriasLines.Add("| ``$($card.Id)`` | $($card.Name) | $(Format-MarkdownText $card.Type) | $($card.Rarity) | $($card.Expend) | $(Format-MarkdownText $card.Description) |")
    }
}

$specialCards = @($terriasCards | Where-Object { [string]::IsNullOrWhiteSpace($_.PackBelong) } | Sort-Object Id)
$terriasLines.Add("")
$terriasLines.Add("### 角色、系统与模式专用牌（$($specialCards.Count) 张）")
$terriasLines.Add("")
$terriasLines.Add("这些牌包括角色主动技能、星谱派生牌、无尽诅咒、动态模板和特殊奖励。它们没有常规 ``PackBelong``，不应按普通卡包掉落理解。")
$terriasLines.Add("")
$terriasLines.Add("| 卡牌 ID | 名称 | 类型 | 费用 | 效果 |")
$terriasLines.Add("|---|---|---|---:|---|")
foreach ($card in $specialCards) {
    $terriasLines.Add("| ``$($card.Id)`` | $($card.Name) | $(Format-MarkdownText $card.Type) | $($card.Expend) | $(Format-MarkdownText $card.Description) |")
}

$terriasLines.Add("")
$terriasLines.Add("## Buff 总表")
$terriasLines.Add("")
$buffTypeOrder = @("正面", "负面", "能力", "契印", "特性", "场地")
foreach ($buffType in $buffTypeOrder) {
    $buffs = @($terriasBuffs | Where-Object { $_.Type -eq $buffType } | Sort-Object Id)
    if ($buffs.Count -eq 0) {
        continue
    }
    $terriasLines.Add("### $buffType（$($buffs.Count) 条）")
    $terriasLines.Add("")
    $terriasLines.Add("| Buff ID | 名称 | 稀有度 | 上限 | 衰减（回合／受击／行动） | 效果 |")
    $terriasLines.Add("|---|---|---:|---:|---|---|")
    foreach ($buff in $buffs) {
        $decay = "$($buff.ReducePerTurn)／$($buff.ReducePerAttacked)／$($buff.ReducePerUse)"
        $terriasLines.Add("| ``$($buff.Id)`` | $($buff.Name) | $($buff.Rarity) | $($buff.UpperBound) | $decay | $(Format-MarkdownText $buff.Description) |")
    }
    $terriasLines.Add("")
}

$terriasLines.Add("## 遗物总表")
$terriasLines.Add("")
$terriasLines.Add("| 遗物 ID | 名称 | 稀有度 | 所属卡包 | 效果 |")
$terriasLines.Add("|---|---|---:|---|---|")
foreach ($relic in $terriasRelics) {
    $pack = $packById[$relic.PackBelong]
    $packName = if ($null -ne $pack) { $pack.Name } else { $relic.PackBelong }
    $terriasLines.Add("| ``$($relic.Id)`` | $($relic.Name) | $($relic.Rarity) | $(Format-MarkdownText $packName) | $(Format-MarkdownText $relic.Description) |")
}

$terriasLines.Add("")
$terriasLines.Add("## 祝福与火漆")
$terriasLines.Add("")
$terriasLines.Add("### 祝福（$($terriasBlesses.Count) 条）")
$terriasLines.Add("")
$terriasLines.Add("| 祝福 ID | 名称 | 类型 | 稀有度 | 所属卡包 | 效果 |")
$terriasLines.Add("|---|---|---|---:|---|---|")
foreach ($bless in $terriasBlesses) {
    $pack = if ([string]::IsNullOrWhiteSpace($bless.PackBelong)) { $null } else { $packById[$bless.PackBelong] }
    $packName = if ($null -ne $pack) { $pack.Name } else { "隐藏条目" }
    $terriasLines.Add("| ``$($bless.Id)`` | $($bless.Name) | $(Format-MarkdownText $bless.Type) | $($bless.Rarity) | $(Format-MarkdownText $packName) | $(Format-MarkdownText $bless.Description) |")
}

$terriasLines.Add("")
$terriasLines.Add("### 火漆（$($terriasEnchTags.Count) 条）")
$terriasLines.Add("")
$terriasLines.Add("| 火漆 ID | 名称 | 稀有度 | 所属卡包 | 效果 |")
$terriasLines.Add("|---|---|---:|---|---|")
foreach ($tag in $terriasEnchTags) {
    $pack = if ([string]::IsNullOrWhiteSpace($tag.PackBelong)) { $null } else { $packById[$tag.PackBelong] }
    $packName = if ($null -ne $pack) { $pack.Name } else { $tag.PackBelong }
    $terriasLines.Add("| ``$($tag.Id)`` | $($tag.Name) | $($tag.Rarity) | $(Format-MarkdownText $packName) | $(Format-MarkdownText $tag.Description) |")
}

$terriasLines.Add("")
$terriasLines.Add("## 难度词条")
$terriasLines.Add("")
$terriasLines.Add("| 词条 ID | 名称 | 分类 | 最大层数 | 效果 |")
$terriasLines.Add("|---|---|---|---:|---|")
foreach ($hard in $terriasHard) {
    $terriasLines.Add("| ``$($hard.Id)`` | $($hard.Name) | $(Format-MarkdownText $hard.Type) | $($hard.MaxCount) | $(Format-MarkdownText $hard.Description) |")
}

$terriasLines.Add("")
$terriasLines.Add("## 专属模式与系统")
$terriasLines.Add("")
$terriasLines.Add("| 内容 | 当前玩家流程 | 详细文档 |")
$terriasLines.Add("|---|---|---|")
$terriasLines.Add("| 日耀回忆 | 选择 11 张开局卡、分配 50 点本源、选满 15 个祝福，经历三层固定回忆；是否持有【炽冕崩落】决定是否进入白曜圣女隐藏终局。 | [日耀回忆模式](../Terrias/modules/05-日耀回忆模式.md) |")
$terriasLines.Add("| 无尽之渊 | 每层配置 6 槽地图，1-6 层为潜行阶段，第 7 层起进入无尽阶段；战斗、奖励、注视和深渊震荡持续累积。 | [地图循环](../Terrias/modules/06-无尽之海模式与地图循环.md)、[压力与奖励](../Terrias/modules/07-无尽深渊压力与奖励体系.md) |")
$terriasLines.Add("| 精灵系统 | 使用精灵球按目标已损失生命检定捕获，成功后生成可持久化精灵卡；战斗中精灵与投影共用召唤位。 | [精灵球捕获与精灵召唤](../Terrias/modules/08-精灵球捕获与精灵召唤.md) |")
$terriasLines.Add("| 投影、百变、心变 | 分别提供角色投影、角色形态复制和敌方控制入口；对应卡牌位于【更多的次元】。 | [模块覆盖矩阵](../Terrias/00-module-coverage-matrix.md) |")
$terriasLines.Add("")
$terriasLines.Add("## 专属敌人")
$terriasLines.Add("")
$terriasLines.Add("| 敌人 ID | 名称 | 等级 | 基础生命 | 主要出现位置 |")
$terriasLines.Add("|---|---|---:|---:|---|")
$enemyLocations = @{
    "Terrias_terrias_boss_orbit_mirror_array" = "日耀回忆第二层固定首领"
    "Terrias_terrias_boss_second_sun_last_day" = "日耀回忆第三层终局前首领"
    "Terrias_terrias_boss_saint_wuna" = "持有【炽冕崩落】时开启的隐藏终局"
}
foreach ($enemy in $terriasEnemies) {
    $terriasLines.Add("| ``$($enemy.Id)`` | $($enemy.Name) | $($enemy.Level) | $($enemy.Hp) | $($enemyLocations[$enemy.Id]) |")
}

$terriasLines.Add("")
$terriasLines.Add("## 阅读与维护口径")
$terriasLines.Add("")
$terriasLines.Add("- 本页是玩家侧内容目录；行为细节有冲突时，以当前 ``Terrias/Data``、``Terrias/Text``、``Terrias-Dev`` 与已打包 DLL 为准。")
$terriasLines.Add("- 卡牌、Buff、遗物和祝福数量来自同一运行时快照，避免把说明行、未加载行或其他 MOD 内容混入统计。")
$terriasLines.Add("- ``更多的次元`` 当前表内说明仍为占位文本，本页只按实际卡牌能力概括其定位，不把占位文案当成正式设计说明。")
$terriasLines.Add("- 技术文档中的旧数量可能早于本次运行时导出；更新内容规模时应重新运行 ``tools/Export-GameAndTerriasContentDocs.ps1``。")

$terriasOutput = Join-Path $RepoRoot "docs\Terrias扩展内容\Terrias扩展内容总览.md"
Write-Utf8NoBom -Path $terriasOutput -Lines $terriasLines
}

Write-Host "Generated:"
Write-Host "  $gameOutput"
Write-Host "  $terriasOutput"
