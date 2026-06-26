# Solar Memory 事件替换与固定地图节点实现说明

本文档整理 SunExp 当前“日耀回忆 / Solar Memory”的地图事件替换、固定地图节点、整备入口与终局结算路由。它描述的是当前实现链路，不是未来草案。

## 总体方案

日耀回忆没有重写原生地图生成器，也没有把普通事件池整体替换成 SunExp 事件池。

当前实现采用：

1. CSV 中声明可被引用的 Map / EventList / Text 行。
2. `SunExpIds` 统一维护地图 ID、事件 ID、Boss Level ID 的映射。
3. 原生 `NormalMapManager` 先照常生成地图。
4. Hook 在原生生成后只覆盖当前层的 `MapTree.DefaultNode` 与 `MapTree.SelectNode` 层段。
5. `MapSelectUI` 展示前再锁一次固定槽位。
6. `MapManager` 同步地图数组前修正 `maps[]` 与 `mapData[]`。
7. EventList 选项只调用稳定的 C# 入口，由 `EventScripts` 和运行时状态机决定后续行为。

核心目标是：固定剧情节点只在日耀回忆模式中出现，普通冒险和普通事件池不被污染。

## 数据层：Map 行使用占位 NodeId

文件：

- `SunExp/Data/Map/sunexp.csv`
- `SunExp/Text/Map/sunexp.csv`

日耀回忆剧情地图节点在 Map 表中使用 `Breaks_*` 作为占位 NodeId：

```csv
solar_memory_black_sun_after,Event,Breaks_solar_memory_black_sun_after,-1
solar_memory_second_sun,Event,Breaks_solar_memory_second_sun,-1
solar_memory_saint_daily,Event,Breaks_solar_memory_saint_daily,-1
solar_memory_polluted_light,Event,Breaks_solar_memory_polluted_light,-1
solar_memory_grief_struggle,Event,Breaks_solar_memory_grief_struggle,-1
solar_memory_above_sacred_wheel,Event,Breaks_solar_memory_above_sacred_wheel,-1
```

这些行的作用是提供地图牌文本、类型和可查询的 Map ID。它们不会直接把真实 EventList ID 暴露给原生普通事件池。

运行时创建固定剧情节点时，会把 `NodeId` 改写成真实事件：

```text
SunExp_sunexp_Sub_solar_memory_black_sun_after
SunExp_sunexp_Sub_solar_memory_second_sun
...
```

固定 Boss 地图节点则直接是 Fight 行：

```csv
solar_memory_boss_orbit_mirror_array,Fight,SunExp_sunexp_level_orbit_mirror_array,-1
solar_memory_boss_second_sun_last_day,Fight,SunExp_sunexp_level_second_sun_last_day,-1
solar_memory_boss_saint_wuna,Fight,SunExp_sunexp_level_saint_wuna,-1
```

## ID 层：SunExpIds 统一维护映射

文件：

- `SunExp-Dev/Infrastructure/SunExpIds.cs`

关键字段：

```csharp
public static readonly string[] SolarMemoryEventIds;
public static readonly string[] SolarMemoryFullEventIds;
public static readonly string[] SolarMemoryMapIds;
public static readonly string[] SolarMemoryShortMapIds;
public static readonly string[] SolarMemoryLayerNames;
public const int SolarMemoryMaxLayer = 3;
```

映射关系：

| 逻辑 | 数据 |
| --- | --- |
| 地图牌 ID | `SolarMemoryMapIds` / `SolarMemoryShortMapIds` |
| 事件入口 ID | `SolarMemoryFullEventIds` / `SolarMemoryEventIds` |
| 地图层标题 | `SolarMemoryLayerNames` |
| 固定 Boss Map / Level / Enemy | `SolarBoss*MapId`、`SolarBoss*LevelId`、`SolarBoss*EnemyId` |
| 整备状态 | `SolarMemoryPrepStepKey` 与旧布尔 key |

后续新增固定事件或固定 Boss 时，应先扩展这里的 ID 常量，再修改 Map / EventList / Text 和节点池逻辑。

## EventList 层：使用 Sub 事件和 C# 入口

文件：

- `SunExp/Data/EventList/sunexp.csv`
- `SunExp/Text/EventList/sunexp.csv`
- `SunExp-Dev/Scripting/EventScripts.cs`

日耀回忆事件全部使用 `Sub_` 前缀，例如：

```csv
Sub_solar_memory_black_sun_after
Sub_solar_memory_second_sun
Sub_solar_memory_saint_daily
Sub_solar_memory_polluted_light
Sub_solar_memory_grief_struggle
Sub_solar_memory_above_sacred_wheel
```

这样它们不会作为普通顶层事件被随机抽取。地图节点进入这些事件，是由运行时把 Map 节点的 `NodeId` 写成完整事件 ID 实现的。

事件选项不写长逻辑，只调用 C#：

```csharp
CS.SunExp.Dll.Scripting.EventScripts.ContinueSolarMemory();
CS.SunExp.Dll.Scripting.EventScripts.OpenSolarMemoryDeck();
```

整备流程由模式入口启动与恢复，不再通过 `Sub_solar_memory_start` EventList 行承载。第三层完成后由运行时直接进入结算 UI，不再打开 `Sub_solar_finale_*` 旧终幕事件。

## Hook 层：原生生成后覆盖当前层

文件：

- `SunExp-Dev/Hooks/SolarMemoryModeRuntime.cs`

关键 Hook：

```csharp
RegisterBefore(modConfig, "NormalMapManager.RandomGenerate", CaptureSolarMemoryGenerationState);
RegisterAfter(modConfig, "NormalMapManager.GeneratrMap", RewriteSolarMemoryMap);
RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", EnsureSolarMemoryMapBeforeSelect);
RegisterBefore(modConfig, "MapManager.CmdSelectMap", RepairSolarMemoryMapSelection);
RegisterBefore(modConfig, "MapManager.RpcUpdateMap", RepairSolarMemoryMapSelection);
RegisterAfter(modConfig, "MapSelectUI.ShowMap", ReapplySolarMemoryFixedSlotLocks);
```

分工：

| Hook | 职责 |
| --- | --- |
| `NormalMapManager.RandomGenerate before` | 捕获原生生成前的事件记录数量 |
| `NormalMapManager.GeneratrMap after` | 主应用点，覆盖当前层 `MapTree` |
| `MapSelectUI.ReadyToSelect before` | UI 消费 SelectNode 前防御性重写 |
| `MapSelectUI.ShowMap after` | 重建固定槽位视觉 |
| `MapManager.*SelectMap*` / `RpcUpdateMap` | 修正网络/同步数组 |
| `Fight_Win.ResetStates after` | 固定 Boss 战后结算与终局路由 |
| `NormalMapManager.ReadyToChangeMap before` | 到达终层后进入日耀结算 |

注意：控制点在 `MapTree` 层，不是纯 UI 层。`ReadyToSelect` 消费的是 `SelectNode`，所以只改 UI 会导致选择/同步阶段仍拿到旧节点。

## 节点池 Factory：生成当前层目标节点

文件：

- `SunExp-Dev/Mechanics/SolarMemoryMapNodePoolFactory.cs`
- `SunExp-Dev/Mechanics/SolarMemoryMapNodePool.cs`

入口：

```csharp
public static SolarMemoryMapNodePool GenerateLayer(NormalMapManager manager, MapTree tree)
```

当前层计算：

```csharp
var layer = ClampLayer(manager.Level / 6);
```

段长度计算遵守原生变量：

```csharp
DefaultLayerSegmentSize() => 2 + GameVar.ExLockDes
SelectLayerSegmentSize() => 8 - GameVar.ExDeleteDes
```

固定槽位：

```csharp
OpeningSlotIndex = 0
MidLayerSlotIndex = 3
PenultimateSlotIndex = 4
EndingSlotIndex = 5
```

生成规则：

1. 每层默认路径开端槽放本层 opening story event。
2. 指定层的固定槽放固定剧情 Boss。
3. 普通 Boss 槽从扩展 Boss 池抽取。
4. 扩展 Boss 池排除日耀固定剧情 Boss。
5. 抽取失败时 fallback 到 `tree.TypeGenerate("首领")`。

事件节点创建时会覆盖字段：

```csharp
node.data["Id"] = mapId;
node.data["Type"] = "Event";
node.data["Note"] = "普通事件";
node.data["NodeId"] = eventId;
node.data["Level"] = "-1";
```

Boss 节点创建时会从 Map 行生成 Fight 节点，并把 `NodeId` 指向 Level ID。

## Applier：只写当前层段

文件：

- `SunExp-Dev/Mechanics/SolarMemoryMapNodePoolApplier.cs`

入口：

```csharp
public static bool ApplyToCurrentLayer(NormalMapManager manager, string source, bool trimEventRecord)
```

它不会重建整个 `MapTree`，只覆盖当前层片段：

```csharp
defaultStart = pool.Layer * pool.DefaultSegmentSize;
selectStart = pool.Layer * pool.SelectSegmentSize;
```

然后分别写入：

```csharp
tree.DefaultNode[defaultStart + i] = pool.DefaultNodes[i];
tree.SelectNode[selectStart + i] = pool.SelectNodes[i];
```

额外处理：

- 如果原生节点是 Break 节点，非中段固定槽会保留。
- 使用 `EquivalentNode` 避免重复写入。
- `trimEventRecord == true` 时回滚原生生成期间新增的普通事件记录。

事件记录回滚是必要的：原生生成仍然可能抽普通事件并写事件记录，但日耀回忆最终会覆盖这些节点。如果不回滚，存档事件记录会出现“玩家没有真正进入过的事件”。

## 固定槽位锁定与视觉重建

文件：

- `SunExp-Dev/Hooks/SolarMemoryModeRuntime.cs`

固定槽位由 `FixedNodeSpecs(layer)` 给出：

| 层 | 固定槽位 |
| --- | --- |
| layer 0 | opening event；ending event |
| layer 1 | opening event；mid event；ending boss `白曜镜阵·三千环日镜` |
| layer 2 | opening event；mid event；penultimate boss `无慈第二日轮·终日态`；ending boss `白曜圣女·乌娜` |

`CreateFixedNodeData` 会把事件槽写成：

```csharp
Type = "Event"
NodeId = SolarMemoryFullEventIds[eventIndex]
Level = "-1"
```

Boss 槽写成：

```csharp
Type = "Fight"
NodeId = fixed boss LevelId
Level = "-1"
```

`EnsureFixedSlotVisual` 会清理对应槽位已有 MapItem，并按 `Type + "Prefab"` 重建地图牌视觉，避免 UI 已经创建旧节点后显示错位。

## 同步数组修正

文件：

- `SunExp-Dev/Hooks/SolarMemoryModeRuntime.cs`

修正入口：

```csharp
RepairSolarMemoryMapSelection(ModHookContext context)
```

它遍历 Hook 参数中的 `string[] maps` 与 `string[] mapData`，对固定槽位执行：

```csharp
maps[slot] = expectedMapId;
mapData[slot] = expectedNodeId;
```

对事件槽：

```text
expectedMapId = SolarMemoryMapIds[eventIndex]
expectedNodeId = SolarMemoryFullEventIds[eventIndex]
```

对 Boss 槽：

```text
expectedMapId = fixed boss map id
expectedNodeId = fixed boss level id
```

这个修正是运行时方案的最后一道保险。没有它，UI 可能显示固定节点，但选择或联机同步仍可能使用原生生成节点。

## 整备入口与开始连战

文件：

- `SunExp-Dev/Hooks/SolarMemoryPreparationRuntime.cs`

整备状态机：

```text
DeckSelection -> OriginAllocation -> BlessingSelection -> Complete
```

状态持久化：

```text
SunExp_SolarMemoryPrepStep
```

兼容旧 key：

```text
SolarMemoryDeckConfiguredKey
SolarMemoryStarterDeckAppliedKey
SolarMemoryOriginConfiguredKey
SolarMemoryBlessConfiguredKey
SolarMemorySetupFinishedKey
```

整备流程由模式入口创建新 run 时启动；重新进入整备 UI 时由 `SolarMemoryPreparationRuntime.StartOrResume()` 回到当前缺失步骤。旧 `Sub_solar_memory_start` 事件已退休，不再作为开始连战入口。

## 终局与隐藏 Boss

文件：

- `SunExp-Dev/Hooks/SolarMemoryModeRuntime.cs`

流程：

1. 击破 `无慈第二日轮·终日态`。
2. 若当前卡组拥有关键卡 `炽冕崩落`，继续进入固定 Boss 节点 `solar_memory_boss_saint_wuna`。
3. 若没有关键卡，则直接进入结算 UI。
4. 击破 `白曜圣女·乌娜` 后直接进入结算 UI。

旧 `Sub_solar_finale_*` 终幕事件已退休。隐藏战不是普通首领池的一部分，它由固定地图节点流程承载。

## 后续改动检查清单

新增或调整日耀固定地图节点时，至少检查：

- `SunExp-Dev/Infrastructure/SunExpIds.cs` 是否补齐 Map / Event / Level ID。
- `SunExp/Data/Map/sunexp.csv` 与 `SunExp/Text/Map/sunexp.csv` 是否同步。
- `SunExp/Data/EventList/sunexp.csv` 与 `SunExp/Text/EventList/sunexp.csv` 是否同步。
- 事件 ID 是否使用 `Sub_`，避免进入普通随机事件池。
- CSV 脚本列是否只调用 `CS.SunExp.Dll.Scripting.*`。
- `SolarMemoryMapNodePoolFactory` 是否正确生成目标节点。
- `SolarMemoryMapNodePoolApplier` 是否只覆盖当前层段。
- `FixedNodeSpecs` 是否锁定 UI 可见槽位。
- `RepairSolarMemoryMapSelection` 是否同步修正 `maps[]` 与 `mapData[]`。
- 固定剧情 Boss 是否被 `IsSolarMemoryFixedStoryBoss` 排除出扩展 Boss 池。
- 第三层终局是否直接结算，且不会重新打开旧 `Sub_solar_finale_*` EventUI。

推荐验证命令：

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-event-dev\scripts\validate-sunexp-events.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

文档或注释-only 修改可不运行完整验证；行为、CSV、DLL 或测试脚本改动后应运行完整验证栈。
