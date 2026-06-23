# SunExp 遗留内容清理筛查清单

生成日期：2026-06-23

## 背景

本清单用于记录 SunExp 当前已存于 MOD 数据中、但疑似未被当前版本启用的遗留内容，覆盖范围包括：

- 遗留卡牌
- 遗留遗物
- 日耀事件，也就是旧版会给卡牌、遗物或祝福的事件链

本轮只做筛查和记录，不直接删除数据或修改运行时代码。

## 结论摘要

- 明确可列入清理候选的卡牌：7 张。
- 明确可列入清理候选的旧日耀事件：`solar_event` 地图入口，以及 `Sub_wuna_event_01` 至 `Sub_wuna_event_06`、`Sub_wuna_event_repeat` 事件链。
- 暂未发现需要单独删除的遗物本体。13 件日耀遗物当前都挂在 3 个启用的 `Normal` 日耀卡包下。
- `dusk_afterheat_recovery` 是黄昏伙伴的技术占位祝福，当前代码会从普通祝福选择池排除，并通过伙伴逻辑在战斗中授予特性，不应归入本轮旧日耀事件清理。

## 遗留卡牌候选

这些卡牌在 `SunExp/Data/Card/sunexp.csv`、`SunExp/Text/Card/sunexp.csv`、图片资源和 `CardScripts` 中仍有定义，但 ID 以 `*` 开头，按当前数据规则属于随机池隐藏卡。当前日耀回忆起始牌组构建逻辑也显式过滤 `id.StartsWith("*")`，未找到当前版本会把它们加入牌组或作为奖励发放的入口。

| ID | 中文名 | 类型 | 稀有度 | 费用 | 所属卡包 | 建议处理 |
| --- | --- | --- | --- | --- | --- | --- |
| `*canopy_return` | 天幕再临 | 技能牌 | 2 | 1 | 日耀：天幕 | 删除 Data/Text/图片/脚本分支，或转为明确启用内容 |
| `*solar_phase_tuning` | 日相校准 | 技能牌 | 2 | 1 | 日耀：星火 | 删除 Data/Text/图片/脚本分支，或转为明确启用内容 |
| `*radiant_oath` | 启辉誓言 | 技能牌 | 1 | 0 | 日耀：星火 | 删除 Data/Text/图片/脚本分支，或转为明确启用内容 |
| `*solar_scorching_light` | 日耀灼光 | 攻击牌 | 1 | 1 | 日耀：烬冠 | 删除 Data/Text/图片/脚本分支，或转为明确启用内容 |
| `*solar_origin_core` | 日耀源核 | 能力牌 | 2 | 1 | 日耀：星火 | 删除 Data/Text/图片/脚本分支，或转为明确启用内容 |
| `*gathered_flame_cycle` | 聚炎轮转 | 能力牌 | 2 | 2 | 日耀：烬冠 | 删除 Data/Text/图片/脚本分支，或转为明确启用内容 |
| `*afterglow_omen_card` | 残光病兆 | 能力牌 | 2 | 2 | 日耀：天幕 | 删除 Data/Text/图片/脚本分支，或转为明确启用内容 |

相关位置：

- `SunExp/Data/Card/sunexp.csv`
- `SunExp/Text/Card/sunexp.csv`
- `SunExp/ModResource/Images/Card/SunExp/`
- `SunExp-Dev/Scripting/CardScripts.cs`
- `SunExp-Dev/Hooks/SolarMemoryStarterDeckRuntime.cs`

## 旧日耀事件候选

旧的 `solar_event` 地图入口仍存在于 `SunExp/Data/Map/sunexp.csv`，对应 `Breaks_solar_event`。但当前 `SolarEventRuntime` 已标记为 retired，且方法体为空；`RuntimeHooks.Initialize` 当前没有注册该旧运行时，只注册了黄昏伙伴、日耀回忆模式、内容隔离、起始牌组等新流程。

因此，下列旧事件链可以作为清理候选：

| ID | 中文名 | 当前旧奖励脚本 | 建议处理 |
| --- | --- | --- | --- |
| `solar_event` | 日耀事件地图入口 | `Breaks_solar_event` | 删除 Map/Text Map 入口，或确认完全迁移后移除旧运行时占位 |
| `Sub_wuna_event_01` | 无日之国 | 给 `morning_shard` 或 `blessing_8` | 删除 Data/Text EventList 行及 C# 进度逻辑 |
| `Sub_wuna_event_02` | 秩序化光辉 | 给 `sun_orbit_mirror` 或 `blessing_8` | 删除 Data/Text EventList 行及 C# 进度逻辑 |
| `Sub_wuna_event_03` | 光中的污染 | 给 `solar_prism` 或 `blessing_20` | 删除 Data/Text EventList 行及 C# 进度逻辑 |
| `Sub_wuna_event_04` | 将灾厄引入自身 | 给 `gathered_flame_charm` 或 `ember_cloak_lining` | 删除 Data/Text EventList 行及 C# 进度逻辑 |
| `Sub_wuna_event_05` | 破碎冠冕 | 给 `blazing_crown_collapse` 或 `coronation_throne` | 删除 Data/Text EventList 行及 C# 进度逻辑 |
| `Sub_wuna_event_06` | 曜日魔女 | 给 `blazing_crown_heart` 或 `spark` | 删除 Data/Text EventList 行及 C# 进度逻辑 |
| `Sub_wuna_event_repeat` | 日耀笔记 | 给 `blessing_8` | 删除 Data/Text EventList 行及 C# 进度逻辑 |

相关位置：

- `SunExp/Data/Map/sunexp.csv`
- `SunExp/Text/Map/sunexp.csv`
- `SunExp/Data/EventList/sunexp.csv`
- `SunExp/Text/EventList/sunexp.csv`
- `SunExp-Dev/Scripting/EventScripts.cs`
- `SunExp-Dev/Hooks/SolarEventRuntime.cs`
- `SunExp-Dev/Hooks/RuntimeHooks.cs`
- `SunExp-Dev/Infrastructure/SunExpIds.cs`

## 遗物筛查结论

当前 `SunExp/Data/Relic/sunexp.csv` 中共有 13 件日耀遗物，均有 `PackBelong`，且分别挂在三个启用的日耀卡包下：

| ID | 中文名 | 所属卡包 |
| --- | --- | --- |
| `morning_shard` | 晨辉碎片 | 日耀：星火 |
| `sun_orbit_mirror` | 环日镜 | 日耀：星火 |
| `solar_phase_dial` | 日相刻盘 | 日耀：星火 |
| `solar_prism` | 日心棱镜 | 日耀：星火 |
| `coronation_throne` | 授冕圣座 | 日耀：星火 |
| `ember_cloak_lining` | 烬衣衬布 | 日耀：烬冠 |
| `miniature_sunwheel` | 小型日轮 | 日耀：烬冠 |
| `gathered_flame_charm` | 聚炎护符 | 日耀：烬冠 |
| `ash_charm` | 灰烬护符 | 日耀：烬冠 |
| `sun_bottle` | 太阳瓶 | 日耀：天幕 |
| `blazing_crown_heart` | 炽冠圣心 | 日耀：天幕 |
| `blazing_sundial` | 曜阳日晷 | 日耀：天幕 |
| `burning_calamity_wind_belt` | 燃灾风带 | 日耀：天幕 |

这些遗物被旧日耀事件奖励引用过，但本体仍属于当前启用卡包内容。清理旧事件时，应移除事件奖励引用；不建议因此删除遗物本体。

相关位置：

- `SunExp/Data/Relic/sunexp.csv`
- `SunExp/Text/Relic/sunexp.csv`
- `SunExp-Dev/Scripting/RelicScripts.cs`

## 暂不归入本轮清理的内容

### 3 个日耀卡包

`SunExp/Data/CardPack/sunexp.csv` 中的 3 个卡包均为 `Normal` 类型，且 `ModConfig.json` 当前说明中明确写入了“新增3个日耀卡包”“新增30张日耀卡牌”“新增13件日耀遗物”。本轮不将卡包本身作为遗留项。

- `cardpack_radiant_spark`
- `cardpack_ember_crown`
- `cardpack_solar_canopy`

### 黄昏伙伴占位祝福

`dusk_afterheat_recovery` 虽然位于 `Data/Blessing`，但它是黄昏伙伴的技术占位祝福：

- `DuskPartnerRuntime` 会移除普通流程中的占位祝福。
- 战斗开始时，如果当前伙伴是黄昏，则授予 `dusk_afterheat_recovery_trait`。
- `SolarMemoryBlessingPickerRuntime` 也会把该祝福识别为技术祝福并从选择池排除。

因此它不是旧日耀事件遗留项。

## 建议后续清理步骤

1. 先清理旧日耀事件链：
   - 删除 `solar_event` 的 Map/Text Map 行。
   - 删除 `Sub_wuna_event_*` 的 Data/Text EventList 行。
   - 删除 `EventScripts` 中旧的 `Init`、`RewardCard`、`RewardRelic`、`RewardBless`、`RepeatReward`、`BeginWunaEvent`、`WunaEventProgressKey` 相关逻辑。
   - 删除或进一步压缩 `SolarEventRuntime` retired 占位类。

2. 再清理 7 张 `*` 前缀卡：
   - 删除 Data/Text Card 行。
   - 删除对应卡图资源。
   - 删除 `CardScripts` 中对应 `case` 和私有实现方法。
   - 同步更新 `SunExp-Dev/Docs/EffectTables.md` 等效果表文档。

3. 最后做验证：
   - `tools\Build-SunExpDll.ps1`
   - `tools\Test-SunExpCSharp.ps1`
   - `.codex\skills\sunexp-event-dev\scripts\validate-sunexp-events.ps1`
   - `.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1`

