# 《魔女：终末旅途》卡包 Mod CSV 开发指南

本指南面向“卡包”类 Mod，整理 `Card`、`CardPack`、`Buff`、`Relic` 四类 CSV 的字段含义、可填内容、格式约定和常见坑。依据当前仓库中的官方参考 `apocalyptic-journey-mod-tutorial`，并对照当前 `SunExp` 卡包实践整理。

## 1. 通用规则

### 1.1 Data 与 Text 的分工

- `Data/`：机制、数值、脚本、图标、归属关系。
- `Text/`：名称、描述、风味文本、本地化。
- 同一类物品通常需要 `Data` 与 `Text` 各有一行相同的本地 `Id`。
- 第 1 行必须是表头；第 2 行通常是字段注释，建议保留。
- 不要自行改表头顺序或列名；以官方模板或当前项目已验证表头为准。

### 1.2 Id 拼接规则

官方规则：Mod 新增物品运行时 Id 形如：

```text
ModName_FileName_Id
```

例如：

```text
ModName = SunExp
文件名 = sunexp.csv
本地 Id = solar_radiance
完整 Id = SunExp_sunexp_solar_radiance
```

填写原则：

- 同一个 CSV 的 `Data`/`Text` 对应行使用本地 `Id`。
- 跨表引用 Mod 新物品时，写完整 Id。
- `PackBelong` 写完整卡包 Id。
- 引用原版 Id 时，官方说明可用 `DataId.xxx`；在 Lua/CSV 中也常直接使用原版字符串，如 `buff_burn`。
- `Id` 前带 `*` 表示不会进入随机池；不带 `*` 会正常进入池子。仅在确实需要排除随机获得时使用。

### 1.3 Script 列格式

- 所有带 `Script` 后缀的列都写 Lua 逻辑，`self` 是 `ScriptExecutor`。
- 官方 `Scripts/Lib/DataConfigs` 中的原版脚本多为 C# 风格，只能参考逻辑，不能原样放进 Mod。
- C# 风格 `AddBuff(id, level)` 要改成 Lua 风格 `self:AddBuff(id, level)`。
- C# 字典写法 `Vars["BaseScript"] = "..."` 要改成 `self.Vars:set_Item("BaseScript", "...")`。
- 脚本字段是 CSV 字段；如果包含逗号、换行或双引号，整格要用双引号包住，内部双引号写成 `""`。

常用 Lua 片段：

```lua
self.Vars:set_Item("BaseScript", "AttackCardItem")
self.Vars:set_Item("CanSelf", "False")
self:SetStatus("Target")
self:Damage("5")
self:AddBuff("buff_burn", "1")
```

### 1.4 资源路径

- Mod 资源通常写为 `Mods/<ModName>/ModResource/...`。
- 图标路径通常不写扩展名，例如 `Mods/SunExp/ModResource/Images/Card/SunExp/spark`。
- 原版资源路径参考 `Scripts/Lib/DataConfigs` 中的原表。

## 2. Card 卡牌 CSV

### 2.1 `Data/Card/*.csv`

表头：

```csv
Id,Rarity,Expend,Tag,InitScript,DrawScript,UseScript,DropScript,Icon,Effects,Action,PackBelong
```

| 列 | 必填 | 可填内容 / 格式 | 说明 |
| --- | --- | --- | --- |
| `Id` | 是 | 英文/数字/下划线；可带 `*` | 本地卡牌 Id。不要和同文件其他行重复。 |
| `Rarity` | 是 | `1`、`2`、`3`；原版也常见稀有度数值 | 稀有度/获取权重相关。当前卡牌参考表观察到 `1/2/3`。 |
| `Expend` | 是 | 整数字符串，如 `0`、`1`、`2`、`3` | 使用费用。动态费用可配合 `Vars`，但基础值仍建议填写。 |
| `Tag` | 否 | 逗号分隔标签，如 `Burnout,Retain` | 官方/当前观察到 `Ability`、`Ascension`、`Burnout`、`Combo`、`Curse`、`Fission`、`Froze`、`Inherent`、`Instant`、`Nihility`、`Recycle`、`Retain`、`Ritual`、`SpellComponents`、`Unusable`。新增标签需确认游戏是否识别。 |
| `InitScript` | 是 | Lua 脚本 | 初始化/刷新显示。必须设置 `BaseScript`。可设置 `CanSelf`、`Usable`、`ExCost` 等 `Vars`。 |
| `DrawScript` | 否 | Lua 脚本 | 抽到卡牌时执行。 |
| `UseScript` | 是 | Lua 脚本 | 使用卡牌时执行的主效果。 |
| `DropScript` | 否 | Lua 脚本 | 进入弃牌堆时执行。 |
| `Icon` | 建议 | 资源路径，不带扩展名 | 卡图路径。 |
| `Effects` | 否 | 特效路径 | 可空。 |
| `Action` | 否 | `Attack`、`Buff`、`Skill`、`Special` | 动作/表现类型；攻击牌常填 `Attack`，也可按原版参考。 |
| `PackBelong` | 卡包 Mod 建议必填 | 完整卡包 Id | 如 `SunExp_sunexp_cardpack_sunexp_base`。 |

`BaseScript` 必须在 `InitScript` 中设置：

```lua
-- 需要选择目标
self.Vars:set_Item("BaseScript", "AttackCardItem")
self.Vars:set_Item("CanSelf", "False")

-- 不需要选择目标
self.Vars:set_Item("BaseScript", "CommonCardItem")
```

目标选择常见值，来自官方/当前脚本观察：

```text
Self, Target, All, AllTarget, AllExSelf,
AllFriends, AllFriendsExSelf,
AllRandomEnemy1, AllRandomTarget1, AllRandomTarget3
```

### 2.2 `Text/Card/*.csv`

表头：

```csv
Id,是否完成,Type,Note,Name,Name_en,Name_zh-Hant,Name_ja,Description,Description_zh-Hant,Description_en,Description_ja
```

| 列 | 必填 | 可填内容 / 格式 | 说明 |
| --- | --- | --- | --- |
| `Id` | 是 | 对应 `Data/Card` 的本地 Id | 不写完整 Id。 |
| `是否完成` | 建议 | `TRUE` / `FALSE` | 开发标记；成品通常填 `TRUE`。 |
| `Type` | 是 | 显示文本 | 观察到 `攻击牌`、`技能牌`、`能力牌`、`消耗攻击牌`、`消耗技能牌`、`诅咒`。 |
| `Note` | 否 | 任意文本 | 作者备注。 |
| `Name` | 是 | 简体中文 | 显示名称。 |
| `Name_en` | 建议 | 英文 | 英文本地化。 |
| `Name_zh-Hant` | 建议 | 繁中 | 繁中本地化。注意表头使用 `zh-Hant`。 |
| `Name_ja` | 建议 | 日文 | 日文本地化。 |
| `Description` | 是 | 简体中文描述 | 可写 `{buff_id}` 形式引用关键词/Buff。 |
| `Description_zh-Hant` | 建议 | 繁中描述 | 同上。 |
| `Description_en` | 建议 | 英文描述 | 同上。 |
| `Description_ja` | 建议 | 日文描述 | 同上。 |

描述写法：

- 引用 Buff/关键词：`获得1层{SunExp_sunexp_solar_radiance}`。
- 如果脚本用 `AddDescription("1", "Damage", "5")` 风格，描述可用 `{0}`、`{1}` 占位；纯 Lua 直接写固定描述也可。

## 3. CardPack 卡包 CSV

官方参考中，卡包表存在两种形态，需要按当前项目表头填写，不要混用。

### 3.1 官方参考形态：`Text/CardPack/*.csv`

官方 `Scripts/Lib/DataConfigs/Text/CardPack/cardpack.csv` 和模板样例表头：

```csv
Id,Name,Name_zh-Hant,Name_en,Name_ja,Description,Description_zh-Hant,Description_en,Description_ja,Icon,Type
```

| 列 | 必填 | 可填内容 / 格式 | 说明 |
| --- | --- | --- | --- |
| `Id` | 是 | 本地或原版卡包 Id；可带 `*` | 官方原版示例有 `1`、`*4` 等。带 `*` 不进随机池。 |
| `Name` | 是 | 简体中文 | 卡包名。 |
| `Name_zh-Hant` | 建议 | 繁中 | 繁中名。 |
| `Name_en` | 建议 | 英文 | 英文名。 |
| `Name_ja` | 建议 | 日文 | 日文名。 |
| `Description` | 是 | 简体中文 | 卡包描述。 |
| `Description_zh-Hant` | 建议 | 繁中 | 繁中描述。 |
| `Description_en` | 建议 | 英文 | 英文描述。 |
| `Description_ja` | 建议 | 日文 | 日文描述。 |
| `Icon` | 建议 | 资源路径 | 卡包图标。 |
| `Type` | 是 | `Basic`、`Expand` | 官方原版观察到这两个值。 |

### 3.2 当前 SunExp 实践形态：`Data/CardPack` + `Text/CardPack`

`Data/CardPack/sunexp.csv`：

```csv
Id,Type,Icon
```

| 列 | 必填 | 可填内容 / 格式 | 说明 |
| --- | --- | --- | --- |
| `Id` | 是 | 本地卡包 Id | 例如 `cardpack_sunexp_base`。完整 Id 会变成 `SunExp_sunexp_cardpack_sunexp_base`。 |
| `Type` | 是 | 当前观察到 `Normal` | `SunExp` 使用 `Normal`。官方 Text/CardPack 原表使用 `Basic/Expand`；两套结构不要硬套。 |
| `Icon` | 建议 | 资源路径，不带扩展名 | 如 `Mods/SunExp/ModResource/Images/CardPack/sunexp`。 |

`Text/CardPack/sunexp.csv`：

```csv
Id,Note,Name,Name_zh-Hant,Name_en,Name_ja,Description,Description_zh-Hant,Description_ja,Description_en
```

| 列 | 必填 | 可填内容 / 格式 | 说明 |
| --- | --- | --- | --- |
| `Id` | 是 | 对应 `Data/CardPack` 的本地 Id | 不写完整 Id。 |
| `Note` | 否 | 任意文本 | 作者备注。 |
| `Name` / 本地化列 | 是/建议 | 卡包名 | 按表头语言填写。 |
| `Description` / 本地化列 | 是/建议 | 卡包说明 | 说明玩法主题和内容边界。 |

卡牌或遗物归属卡包时，在 `PackBelong` 中写完整 Id：

```text
SunExp_sunexp_cardpack_sunexp_base
```

## 4. Buff CSV

### 4.1 `Data/Buff/*.csv`

表头：

```csv
Id,InitScript,ApplyScript,ClearScript,ReducePerTurn,ReducePerAttacked,ReducePerUse,UpperBound,Icon,Type,Rarity,Effects,SoundEffects,Action,CanZero
```

| 列 | 必填 | 可填内容 / 格式 | 说明 |
| --- | --- | --- | --- |
| `Id` | 是 | 英文/数字/下划线；可带 `*` | 本地 Buff Id。跨表引用时用完整 Id。 |
| `InitScript` | 否 | Lua 脚本 | 更新显示/初始化。 |
| `ApplyScript` | 视机制而定 | Lua 脚本 | Buff 生效时执行。持续效果、事件监听多写在这里。 |
| `ClearScript` | 建议 | Lua 脚本 | Buff 清除时执行。若 `ApplyScript` 挂了事件或写了状态变量，应在这里清理。 |
| `ReducePerTurn` | 是 | 整数字符串 | 每回合减少层数。观察到 `0/1/2/10/99/999`。 |
| `ReducePerAttacked` | 是 | 整数字符串 | 每次受击减少层数。观察到 `0/1`。 |
| `ReducePerUse` | 是 | 整数字符串 | 每次行动/使用减少层数。观察到 `0`。 |
| `UpperBound` | 是 | 整数字符串 | 层数上限，如 `1`、`9`、`12`、`999`。 |
| `Icon` | 建议 | 资源路径 | 可用原版图标，如 `Icon/Buff/灼烧`，也可用 Mod 资源。 |
| `Type` | 是 | `正面`、`负面`、`能力`、`特性`、`契印` | 游戏逻辑可能按类型判断，如“负面 Buff”。 |
| `Rarity` | 是 | `1`、`2`、`3`、`4` | Buff 稀有度/分类。 |
| `Effects` | 否 | 特效路径 | 可空。 |
| `SoundEffects` | 否 | 音效路径 | 可空。 |
| `Action` | 否 | 动作/表现字段 | 可空。 |
| `CanZero` | 建议 | `TRUE` / `FALSE` | 是否允许 0 层仍存在。通常填 `FALSE`。 |

持续效果建议：

- 卡牌提供长期效果时，做成 Buff，不要把长期事件直接塞在卡牌里。
- `ApplyScript` 中使用 `self:AddEvent("StartRound", function() ... end)` 等监听。
- 注意避免重复注册事件。可用 `self.Vars` 写 flag/token，在 `ClearScript` 中清掉。

### 4.2 `Text/Buff/*.csv`

表头：

```csv
Id,Note,Name,Name_zh-Hant,Name_en,Name_ja,Description,Description_zh-Hant,Description_ja,Description_en
```

| 列 | 必填 | 可填内容 / 格式 | 说明 |
| --- | --- | --- | --- |
| `Id` | 是 | 对应 `Data/Buff` 的本地 Id | 不写完整 Id。 |
| `Note` | 否 | 任意文本 | 作者备注。 |
| `Name` / 本地化列 | 是/建议 | Buff 名称 | 按语言填写。 |
| `Description` / 本地化列 | 是/建议 | Buff 描述 | 可引用其他 Buff：`{buff_burn}`。 |

## 5. Relic 遗物 CSV

### 5.1 `Data/Relic/*.csv`

表头：

```csv
Id,Rarity,OwnScript,FightScript,Icon,PackBelong
```

| 列 | 必填 | 可填内容 / 格式 | 说明 |
| --- | --- | --- | --- |
| `Id` | 是 | 英文/数字/下划线；可带 `*` | 本地遗物 Id。 |
| `Rarity` | 是 | `1`、`2`、`3`、`4` | 遗物稀有度。 |
| `OwnScript` | 否 | Lua 脚本 | 获得遗物时执行。适合一次性获得效果、初始化永久变量。 |
| `FightScript` | 视机制而定 | Lua 脚本 | 战斗中生效脚本，常用 `AddEvent` 挂事件。 |
| `Icon` | 建议 | 资源路径，不带扩展名 | 遗物图标路径。 |
| `PackBelong` | 卡包 Mod 建议必填 | 完整卡包 Id | 让遗物归属于对应卡包。 |

常用 `FightScript` 模式：

```lua
self:AddEvent("FightStart", function()
    self:SetStatus("Self")
    self:AddBuff("SunExp_sunexp_solar_radiance", "2")
end)
```

如果遗物有“每场战斗一次”“每回合一次”等限制，使用 `self.Vars:set_Item(...)` 保存状态，并在 `FightStart` 或 `StartRound` 重置。

### 5.2 `Text/Relic/*.csv`

表头：

```csv
Id,Note,Series,Tag,Name,Name_zh-Hant,Name_en,Name_ja,Tips,Tips_zh-Hant,Tips_en,Tips_ja,Description,Description_zh-Hant,Description_en,Description_ja
```

| 列 | 必填 | 可填内容 / 格式 | 说明 |
| --- | --- | --- | --- |
| `Id` | 是 | 对应 `Data/Relic` 的本地 Id | 不写完整 Id。 |
| `Note` | 否 | 任意文本 | 作者备注。 |
| `Series` | 建议 | 系列名 | 如 `日耀遗物`；用于分类/显示。 |
| `Tag` | 建议 | 标签文本 | 如 `日耀`、`灼烧`、`防御`。 |
| `Name` / 本地化列 | 是/建议 | 遗物名 | 按语言填写。 |
| `Tips` / 本地化列 | 否 | 风味/剧情文本 | 非机制描述。 |
| `Description` / 本地化列 | 是/建议 | 机制描述 | 准确描述脚本效果。 |

## 6. 常用脚本 API 与事件

常用 `ScriptExecutor` 方法：

```lua
self:SetStatus("Self")
self:SetStatusById(target.InstanceId)
self:Damage("10")
self:ChangeHp("-5")
self:ChangeDefence("8")
self:AddBuff("buff_burn", "2")
self:RemoveBuff("buff_burn")
self:DrawCount("1")
self:ChangePower("1")
self:RunImmediately("buff_burn", "StartRound")
self:AddEvent("Action", function() ... end)
self:AddTempEvent("EndRound", function() ... end)
self:UpdateRelicShow()
```

常用战斗事件，来自官方 README 与原版/当前表观察：

```text
FightStart, StartRound, StartRoundEnd, Action, ActionAfter,
Attack, AttackDone, Hurt, Heal, Damage,
AddPower, CostPower, NoPowerWhenTry,
BurnCard, Shuffle, Dead, Resurrection, EndRound,
Win, Escape, CreateCardItem, ICreateCardItem, EndCreateCardItem
```

官方还说明有参事件主要包括 `HurtData`、`ActionData`、`NewEnemyData`、`DamageData`。写 Lua 时要确认当前 Mod 环境对有参事件的调用方式。

## 7. 推荐开发流程

1. 先确定 `ModConfig.json` 的 `ModName`，以及 CSV 文件名；这会决定所有完整 Id。
2. 先建 `CardPack`，确认完整卡包 Id。
3. 再建 `Buff`，因为卡牌和遗物通常会引用 Buff。
4. 写 `Card`，每张卡先填 `BaseScript`、`Expend`、`PackBelong`，再写 `UseScript`。
5. 写 `Relic`，用 `FightScript` 挂战斗事件。
6. 补齐所有 `Text` 表，多语言列可以先和中文一致，但列不能缺。
7. 检查跨表引用：Mod 自定义物品必须用完整 Id。
8. 检查 CSV 引号：脚本列中所有内部双引号必须成对转义。
9. 进游戏验证卡包显示、卡牌入池、Buff 显示、遗物触发。

## 8. 快速检查清单

- `Data` 与 `Text` 是否都有同一个本地 `Id`？
- `PackBelong` 是否写完整卡包 Id？
- 自定义 Buff 引用是否写完整 Id？
- 原版 Buff/卡牌引用是否确认存在？
- 卡牌是否设置 `BaseScript`？
- 攻击牌是否设置 `CanSelf`，目标逻辑是否合理？
- 持续效果是否做成 Buff？
- `ApplyScript` 注册事件是否会重复触发？
- `ClearScript` 是否清理了事件状态变量？
- 图标路径是否不带 `.png`？
- `Tag` 是否只用了游戏已识别标签？
- `Text` 描述是否和实际脚本一致？
- CSV 脚本列的双引号是否全部转义？
