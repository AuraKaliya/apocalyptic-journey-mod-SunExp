# SunExp 旧事件与旧案清理记录

整理日期：2026-06-26
执行状态：已按修正口径清理

## 本轮口径

- 旧事件：以前配置过、后续从实际玩法里撤下的普通日耀事件链，即 `Sub_wuna_event_01` 到 `Sub_wuna_event_06`，以及 `Sub_wuna_event_repeat`。
- 旧案：日耀回忆中没有配置为当前地图节点的旧终幕事件，即 `Sub_solar_finale_*`。
- 追加清理：`Sub_solar_memory_start` 与 `solar_memory_start` 现在不再使用，也一并删除。
- 保留对象：当前 6 个固定日耀回忆剧情节点与 3 个 Boss 节点。

## 已清理数据

### EventList

已从 `SunExp/Data/EventList/sunexp.csv` 与 `SunExp/Text/EventList/sunexp.csv` 删除：

- `Sub_wuna_event_01`
- `Sub_wuna_event_02`
- `Sub_wuna_event_03`
- `Sub_wuna_event_04`
- `Sub_wuna_event_05`
- `Sub_wuna_event_06`
- `Sub_wuna_event_repeat`
- `Sub_solar_memory_start`
- `Sub_solar_finale_ledger`
- `Sub_solar_finale_second_sun`
- `Sub_solar_finale_saint_gate`
- `Sub_solar_finale_saint`
- `Sub_solar_finale_ending`

### Map

已从 `SunExp/Data/Map/sunexp.csv` 与 `SunExp/Text/Map/sunexp.csv` 删除：

- `solar_event`
- `solar_memory_start`

## 已清理代码

- `SunExp-Dev/Scripting/EventScripts.cs`
  - 删除旧日耀事件链的进度、奖励和重复事件入口。
  - 删除 `Sub_solar_memory_start` 的准备/开局事件入口。
  - 删除 `Sub_solar_finale_*` 旧终幕事件入口。
- `SunExp-Dev/Infrastructure/SunExpIds.cs`
  - 删除 `WunaEvent*`、`SolarEvent*`、`SolarMemoryStart*` 和旧终幕事件 ID / 状态 key。
  - 保留当前日耀回忆固定剧情数组与 Boss ID。
- `SunExp-Dev/Hooks/SolarMemoryModeRuntime.cs`
  - 第三层完成后直接进入结算，不再打开旧终幕 EventUI。
  - 保留 `LegacySolarFinaleMapLevel = 30` 旧存档兜底，在原生 `MapItemInit` 前直接结算，避免历史存档卡在不存在的旧终幕索引上。
- `SunExp-Dev/GameApi/SolarMemoryFlowApi.cs`
  - 删除旧终幕事件打开入口。
- `SunExp-Dev/Mechanics/SolarFinaleStateService.cs`
  - 收缩为 Boss 机制仍使用的名字计数服务。

## 当前保留的日耀回忆节点

| 类型 | 数量 | ID |
|---|---:|---|
| 固定剧情 | 6 | `Sub_solar_memory_black_sun_after`、`Sub_solar_memory_second_sun`、`Sub_solar_memory_saint_daily`、`Sub_solar_memory_polluted_light`、`Sub_solar_memory_grief_struggle`、`Sub_solar_memory_above_sacred_wheel` |
| 固定 Boss | 3 | `solar_memory_boss_orbit_mirror_array`、`solar_memory_boss_second_sun_last_day`、`solar_memory_boss_saint_wuna` |

## 索引风险审查结论

- `SolarMemoryEventIds`、`SolarMemoryFullEventIds`、`SolarMemoryMapIds`、`SolarMemoryShortMapIds` 仍然都是 6 项，对应当前 6 个固定剧情节点。
- `Sub_solar_memory_start` 不在这些数组中，删除不会造成数组位移。
- `Sub_solar_finale_*` 不在固定地图节点数组中，删除后运行时不再打开它们。
- 历史终幕 level 30 存档仍有兜底结算，不会让原生地图初始化读取不存在的候选节点。

## 验收口径

- 数据表里不再出现 `Sub_wuna_event_*`、`Sub_solar_memory_start`、`Sub_solar_finale_*`、`solar_event`、`solar_memory_start`。
- `EventScripts` 不再包含旧事件奖励/进度入口或旧终幕入口。
- 日耀回忆地图隔离仍覆盖 9 个现役专属 Map 行，且这些行保持 `Rarity=7`。
- 构建、架构测试、C# 源码断言、事件校验和总体验证均应通过。
