# SunExp Solar Event Expansion

Use this reference when adding or reviewing the WuNa / Solar Event map-event chain.

## Current files

- `SunExp/Data/Map/sunexp.csv`: adds `solar_event`.
- `SunExp/Text/Map/sunexp.csv`: displays the map card as `日耀事件`.
- `SunExp/Data/EventList/sunexp.csv`: defines `Sub_wuna_event_01` through `Sub_wuna_event_06`.
- `SunExp/Text/EventList/sunexp.csv`: defines event titles, body text, and option text.
- `SunExp/Scripts/Entry.lua`: generated from `_src`; owns reward helpers, WuNa progress helpers, and map-event runtime helpers.

## Runtime model

- Progress is stored in game var `SunExp_WunaEventProgressV2`.
- `SunExp_CreateSolarEventNode()` points the map event to the next event id:
  - progress `0` -> `SunExp_sunexp_Sub_wuna_event_01`
  - progress `1` -> `SunExp_sunexp_Sub_wuna_event_02`
  - ...
  - progress `5` -> `SunExp_sunexp_Sub_wuna_event_06`
- The WuNa events use `Sub_` ids so the ordinary event pool does not randomly draw later story chapters as top-level ordinary events.
- `MapSelectUI.CreateMapItem(range)` is only a display step. It receives a temporary visible range and is not a stable source of final selection state.
- `NormalMapManager.RandomGenerate()` overwrites every generated `Type=Event` node's `NodeId` with a random official `EventList` row, so the Solar Event must be assigned after random generation.
- `SunExp_EnsureSolarEventInCurrentLayer(...)` replaces one node inside the current `MapTree.SelectNode` layer segment. It does not append to `SelectNode`, because the base game reads fixed layer ranges.
- `SunExp_TryRepairSolarEventGeneratedMap(...)` is registered after `NormalMapManager.RandomGenerate` and `NormalMapManager.GeneratrMap`.
- `SunExp_EnsureSolarEventInCurrentLayerFromHook(...)` is registered before `MapSelectUI.ReadyToSelect` so the real SelectNode segment is repaired before the UI reads its range.
- `SunExp_TryRepairSolarEventMapSelection(...)` is registered before both `MapManager.UserCode_CmdSelectMap__String[]__String[]__NetworkConnectionToClient` and `MapManager.UserCode_CmdSelectMapIncludeSender__String[]__String[]__NetworkConnectionToClient` as a server-side fallback; any `SunExp_sunexp_solar_event` / `solar_event` map id has its `mapdata` forced back to the current WuNa event id.
- The same map-data repair is also registered on public `CmdSelectMap` methods plus `TargetUpdateMap` / `RpcUpdateMap`, because first-floor map setup can sync arrays before the final click flow.
- Local validation can prove Lua/CSV syntax, ID references, and text shape; it cannot prove Unity runtime hook argument shape. In-game verification is required for "one Solar Event card appears in every map selection".

## Reward helpers

Use short helper calls in CSV event scripts:

```lua
SunExp_WunaRewardCard(progress, "SunExp_sunexp_card_id")
SunExp_WunaRewardRelic(progress, "SunExp_sunexp_relic_id")
SunExp_WunaRewardBless(progress, "blessing_id")
SunExp_WunaRewardNone(progress)
```

Current helper behavior:

- Grant `100` gold.
- Grant the card, relic, or blessing.
- Advance `SunExp_WunaEventProgress` to at least `progress`.
- End the event.

Event `InitScript` should gate visible choices through:

```lua
SunExp_BeginWunaEvent(self, 1)
```

Use the matching step number for each chapter. Do not use bare `Vars:set_Item(...)` in SunExp CSV scripts; use `self.Vars` or this helper so the event UI can see `Choice1` / `Choice2`.

If balance changes, prefer changing the helper once instead of editing every event script.

## Official blessing IDs used or likely useful

| ID to pass | Name | English | Effect summary |
| --- | --- | --- | --- |
| `blessing_8` | 天使 | Angel | 战斗开始时获得1层自愈 |
| `blessing_20` | 萨满 | Shaman | 战斗开始时随机赋予敌人1层易伤 |
| `blessing_15` | 士官 | Sergeant | 战斗开始时你获得1层锋锐 |
| `blessing_19` | 主教 | Bishop | 战斗开始时你获得4层元素 |
| `blessing_23` | 审判 | Judgement | 战斗开始时获得1层狂暴 |
| `blessing_34` | 太阳 | Sun | 战斗开始时获得2层庇护 |

Official blessing rows live in:

- `apocalyptic-journey-mod-tutorial/ModTemplate/Scripts/Lib/DataConfigs/Data/Blessing/blessing.csv`
- `apocalyptic-journey-mod-tutorial/ModTemplate/Scripts/Lib/DataConfigs/Text/Blessing/blessing.csv`

## Current WuNa event reward table

| Event | Title | Option | Reward helper |
| --- | --- | --- | --- |
| `Sub_wuna_event_01` | 无日之城 | 校准第一缕晨辉 | `SunExp_WunaRewardRelic(1, "SunExp_sunexp_morning_shard")` |
| `Sub_wuna_event_01` | 无日之城 | 向圣庭祈祷 | `SunExp_WunaRewardBless(1, "blessing_8")` |
| `Sub_wuna_event_02` | 秩序化光辉 | 转动环日镜 | `SunExp_WunaRewardRelic(2, "SunExp_sunexp_sun_orbit_mirror")` |
| `Sub_wuna_event_02` | 秩序化光辉 | 记录礼拜时辰 | `SunExp_WunaRewardBless(2, "blessing_8")` |
| `Sub_wuna_event_03` | 光中的污染 | 检验日心棱镜 | `SunExp_WunaRewardRelic(3, "SunExp_sunexp_solar_prism")` |
| `Sub_wuna_event_03` | 光中的污染 | 标记腐坏源头 | `SunExp_WunaRewardBless(3, "blessing_20")` |
| `Sub_wuna_event_04` | 将灾厄引入自身 | 收拢聚炎护符 | `SunExp_WunaRewardRelic(4, "SunExp_sunexp_gathered_flame_charm")` |
| `Sub_wuna_event_04` | 将灾厄引入自身 | 披上烬衣衬布 | `SunExp_WunaRewardRelic(4, "SunExp_sunexp_ember_cloak_lining")` |
| `Sub_wuna_event_05` | 破碎冠冕 | 触碰破碎冠冕 | `SunExp_WunaRewardCard(5, "SunExp_sunexp_blazing_crown_collapse")` |
| `Sub_wuna_event_05` | 破碎冠冕 | 扶起授冕圣座 | `SunExp_WunaRewardRelic(5, "SunExp_sunexp_coronation_throne")` |
| `Sub_wuna_event_06` | 曜日魔女 | 裁定腐坏 | `SunExp_WunaRewardRelic(6, "SunExp_sunexp_blazing_crown_heart")` |
| `Sub_wuna_event_06` | 曜日魔女 | 保存名字与星火 | `SunExp_WunaRewardCard(6, "SunExp_sunexp_spark")` |

## Validation checklist

Run:

```powershell
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

Then manually inspect:

- `Import-Csv SunExp/Text/EventList/sunexp.csv` and verify `TotalDescribe`, `1Describe`, and `2Describe` are aligned.
- Reward IDs in `SunExp/Data/EventList/sunexp.csv` exist in current SunExp card/relic CSVs or official blessing CSVs.
- `SunExp/Scripts/Entry.lua` still registers the reward helpers through `SunExp_RegisterDynamicMethods`.
- In game, verify the Solar Event card appears in map choices and progresses through all six events.

## CSV text gotcha

English commas in unquoted CSV text can shift later columns while Lua validation still passes. Quote comma-bearing fields or use comma-free placeholder English text, then verify with `Import-Csv`.
