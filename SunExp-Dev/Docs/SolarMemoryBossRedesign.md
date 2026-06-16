# Solar Memory Boss redesign

本文档记录 SunExp / 日耀回忆 Boss 线的调整方案。

本轮只保留三个已确认 Boss 概念：

- `白曜镜阵·三千环日镜`
- `无慈第二日轮·终日态`
- `白曜圣女·乌娜`

不新增图片、动画或音频资源。Boss 立绘、敌人动画、行动图标、地图牌图先全部使用官方资源占位；后续只替换资源引用，不改变地图节点、Level、Enemy 和脚本接口。

## 结论

日耀回忆不再改造成“每层一个 SunExp 专属 Boss”的线性 Boss rush，而是保留原生 Boss 池作为主体，并在关键层末尾插入固定剧情首领。

最终结构：

| 位置 | 节点类型 | Boss | 是否进入随机池 | 作用 |
| --- | --- | --- | --- | --- |
| 第二层末尾固定首领节点 | 固定 Fight | 白曜镜阵·三千环日镜 | 否 | 让玩家第一次直面第二日轮的照射系统 |
| 第三层末尾固定首领节点 | 固定 Fight | 无慈第二日轮·终日态 | 否 | 日耀回忆主终局战 |
| 第三层末尾节点完成后 | 事件判断后可选 Fight | 白曜圣女·乌娜 | 否 | 隐藏剧情首领 / 真结局门槛 |
| 其他首领节点 | 随机 Fight | 官方所有层首领池 | 是 | 扩大可见 Boss 池，避免固定按层重复 |

`白曜圣女·乌娜`不进入任何普通首领池。她只由第三层末尾后的剧情判断触发。

## 当前实现分析

### 日耀回忆地图结构

当前地图生成主要在：

```text
SunExp-Dev/Mechanics/SolarMemoryMapNodePoolFactory.cs
```

核心逻辑：

- `LayerFor(manager)` 通过 `manager.Level / 6` 得到当前日耀回忆层级。
- `SolarMemoryMaxLayer = 3`，因此运行层级实际是 `0 / 1 / 2`，对应玩家理解中的第一、第二、第三层。
- 每层会生成一段默认节点和一段可选节点。
- 默认节点的第一个槽位是日耀回忆剧情事件。
- 其他槽位当前通过 `CreateBossChainNode(...)` 创建首领节点。
- `CreateBossChainNode(...)` 目前主路径是 `tree.TypeGenerate("首领")`。

因此，“第二层末尾”和“第三层末尾”在代码上应解释为：

| 玩家层数 | 代码 layer | 固定节点位置 |
| --- | --- | --- |
| 第一层 | `0` | 不插入剧情 Boss |
| 第二层 | `1` | 默认节点段最后一个首领槽 |
| 第三层 | `2` | 默认节点段最后一个首领槽 |

注意：`ExLockDes` 可能改变默认节点段长度，所以固定剧情 Boss 不应该写死为 index 1，而应该取“当前默认节点段的最后一个非事件槽”。

### 原生首领抽取限制

反编译工程中，`MapTree.TypeGenerate("首领")` 并不是“全游戏所有首领随机”。它会读取 Map 表后筛选：

```text
Note == "首领"
```

并且对普通、精英、首领继续加层级过滤：

```text
Map.Level == MapManager.Instance.ModeMapManager.Level / 12
或
Map.Level == -1
```

这解释了当前现象：首领节点牌会根据层数抽取固定层级的首领。

用户需求是“抽取池扩大到所有层的首领池”，因此不能继续把 `tree.TypeGenerate("首领")` 作为主路径。需要新建 SunExp 自己的 Boss 节点抽取方法，绕过 `Map.Level == current / 12` 的层级过滤。

### 第三层完成后的当前结算

当前终局处理在：

```text
SunExp-Dev/Hooks/SolarMemoryModeRuntime.cs
```

现状是：当日耀回忆到达 `SolarMemoryMaxLayer * 6` 后，直接关闭地图 UI 并显示 `GameExitUI`。

这会阻断“第三层末尾后进入事件判断，再从选项中加入乌娜战斗节点”的需求。因此这里必须改成终局路由：

1. 第三层固定 Boss `无慈第二日轮·终日态` 完成。
2. 设置或确认 `SolarFinaleSecondSunDefeatedKey`。
3. 进入事件场景。
4. 事件根据名册/结局变量判断是否出现 `白曜圣女·乌娜` 战斗选项。
5. 玩家选择战斗则进入固定 Fight。
6. 战斗后进入终局事件或结算 UI。

## 地图节点方案

### 固定剧情 Boss

新增三个固定 Map / Level：

| Map ID | Type | NodeId | Level | 说明 |
| --- | --- | --- | --- | --- |
| `solar_memory_boss_orbit_mirror_array` | `Fight` | `SunExp_sunexp_level_orbit_mirror_array` | `-1` | 第二层末尾固定 Boss |
| `solar_memory_boss_second_sun_last_day` | `Fight` | `SunExp_sunexp_level_second_sun_last_day` | `-1` | 第三层末尾固定 Boss |
| `solar_memory_boss_saint_wuna` | `Fight` | `SunExp_sunexp_level_saint_wuna` | `-1` | 事件选项触发的隐藏 Boss |

对应 Text/Map 的 `Note` 应写成 `首领`，这样地图牌和战斗展示能沿用官方首领表现。

`Level.Note` 应包含 `boss`，因为反编译中 `MapItem` 和 `EnemyManager` 会用 Level 的 Note 判断首领框、结算倍率等表现。

### 默认节点插入规则

在 `SolarMemoryMapNodePoolFactory.GenerateLayer(...)` 中增加“末尾固定首领”判断：

```csharp
var defaultSegmentSize = DefaultLayerSegmentSize();
var fixedBossSlot = defaultSegmentSize - 1;

for (var i = 0; i < defaultSegmentSize; i++)
{
    if (i == OpeningSlotIndex)
    {
        defaultNodes.Add(CreateSolarMemoryEventNode(layer, 0));
        continue;
    }

    if (i == fixedBossSlot && TryCreateFixedStoryBossNode(tree, layer, out var fixedBossNode))
    {
        defaultNodes.Add(fixedBossNode);
        continue;
    }

    defaultNodes.Add(CreateExpandedBossPoolNode(tree));
}
```

固定 Boss 对照：

```text
layer 0 -> null
layer 1 -> solar_memory_boss_orbit_mirror_array
layer 2 -> solar_memory_boss_second_sun_last_day
```

这样可以同时满足：

- 第二层末尾固定出现镜阵。
- 第三层末尾固定出现终日态。
- `ExLockDes` 改变默认节点数量时，固定 Boss 仍然在末尾。
- 其他首领节点继续从扩大的官方首领池中抽取。

### 全层首领池

新增方法建议命名：

```csharp
private static MapTree.Node CreateExpandedBossPoolNode(MapTree tree)
```

抽取规则：

1. 从 `GameConfigManager.Instance.GetTable(DataType.Map)` 读取所有 Map 行。
2. 筛选 Text/Map 合并后的 `Note == "首领"`。
3. 筛选 `Type == "Fight"`。
4. 不再按 `Map.Level == currentLayer` 过滤。
5. 排除 `Id` 或 `NodeId` 为空、以 `*` 开头、或明显测试禁用的行。
6. 排除 SunExp 固定剧情 Boss：
   - `solar_memory_boss_orbit_mirror_array`
   - `solar_memory_boss_second_sun_last_day`
   - `solar_memory_boss_saint_wuna`
7. 如果能读到 Level 表，则优先确认 `Level.Note` 包含 `boss`；读不到时允许 fallback，但记录日志。
8. 使用 `tree.treedice` 抽取，保证地图生成仍与原生地图树随机种子一致。
9. 抽取失败时 fallback 到 `tree.TypeGenerate("首领")`。

伪代码：

```csharp
private static MapTree.Node CreateExpandedBossPoolNode(MapTree tree)
{
    var rows = GameConfigManager.Instance.GetTable(DataType.Map).Getlines();
    var candidates = rows
        .Where(IsFightBossMap)
        .Where(row => !IsSolarMemoryFixedStoryBoss(row["Id"]))
        .Where(row => !IsDisabledMapRow(row))
        .ToList();

    if (candidates.Count == 0)
    {
        return tree.TypeGenerate("首领");
    }

    var data = DrawWithTreeDice(candidates, tree.treedice);
    return CreateBossNodeFromMapRow(tree, data);
}
```

这个方法是本次需求的关键点：它保留官方所有层首领，但移除原生 `TypeGenerate("首领")` 的层级约束。

## 乌娜隐藏 Boss 路由

### 判断时机

判断不放在第三层生成时，而放在第三层末尾 Boss 完成后。

推荐时机：

```text
SolarMemoryModeRuntime.FinishSolarMemoryAfterFinalLayer
```

现有逻辑直接结算，需要改成状态机：

| 状态 | 行为 |
| --- | --- |
| 未击破终日态 | 不进入终局结算 |
| 击破终日态且未做隐藏判断 | 进入 `Sub_solar_finale_saint_gate` |
| 玩家选择挑战乌娜 | 进入固定 `solar_memory_boss_saint_wuna` |
| 玩家放弃或不满足条件 | 进入 `Sub_solar_finale_ending` |
| 乌娜战斗完成 | 根据名字资源设置结局 key，再进入 ending |

### 事件设计

新增事件：

```text
Sub_solar_finale_saint_gate
```

作用：第三层最终 Boss 后的即时事件场景。

建议选项：

| 选项 | 条件 | 脚本 | 结果 |
| --- | --- | --- | --- |
| 呼唤仍在闪烁的名字 | `SavedNames >= SolarFinaleHiddenBossNameThreshold` 且终日态已击破 | `EventScripts.EnterSolarFinaleSaintBattle()` | 进入乌娜固定战斗 |
| 让第二日轮沉默 | always | `EventScripts.SkipSolarFinaleSaintBattle()` | 进入终局事件 |

如果需要更强叙事，可以增加第三个选项：

| 选项 | 条件 | 脚本 | 结果 |
| --- | --- | --- | --- |
| 把名字写回白昼 | always | `EventScripts.ChooseWhiteCityEnding()` | 直接偏向 `white_city` |

### 从事件进入战斗的实现路线

反编译中 `EventUI.TryChangeMap()` 的作用是结束事件并回到地图推进流程，它本身不等于“直接加载任意 Fight”。因此不要在事件脚本里硬调 `RpcLoadMap("fight", levelId)`，这会引入联机同步和地图树状态风险。

推荐实现：

1. `EnterSolarFinaleSaintBattle()` 设置变量：

```text
SunExp_SolarFinalePendingSaintBattle = 1
```

2. 调用安全的事件结束/换图路径。
3. `SolarMemoryModeRuntime` 或 `SolarMemoryMapNodePoolFactory` 在检测到该变量时，构造一个只包含 `solar_memory_boss_saint_wuna` 的固定后继节点。
4. 玩家进入该节点后清除 pending 变量。
5. 乌娜战斗胜利脚本设置 ending key。

这比在 EventUI 内直接加载战斗更稳，因为它复用地图树、MapSelectUI 和 MapManager 的正常节点流。

如果最终验证发现事件选项必须“一点就直接进战斗”，可以再补一个受控 API：

```csharp
PlayerApi.TryEnterFixedFightMap(mapId, levelId)
```

但它应作为第二选择，并且要先用反编译工程确认 `MapManager` 的 host/client 同步路径。

## Boss 设计

### 白曜镜阵·三千环日镜

定位：第二日轮的镜面照射系统。

它不是传统怪物，而是仍在校准白昼的国家级设施。它的出现位置是第二层末尾，意味着玩家已经看过日耀回忆中的若干事件，此时第一次发现“灾难不是来自某个入侵者，而是来自仍在正常运转的秩序系统”。

机制方向：

| 机制 | 说明 |
| --- | --- |
| 镜阵校准 | 每回合给予双方少量灼烧或日耀压力 |
| 环日折射 | 玩家或 Boss 单体灼烧过高时，将部分灼烧折射给其他目标 |
| 礼拜时辰 | Boss 定期获得护盾，并根据玩家日耀/灼烧施压 |
| 洁净光束 | 清除一个负面状态，但转化为灼烧或焚身 |

战斗目标：

- 教玩家不要无脑把火堆到单点。
- 让 `灼烧 -> 聚炎 -> 爆发/防御` 的循环进入实战。
- 难度不应高于第三层固定终局 Boss。

占位资源：

- 官方机械、镜阵、灾厄核心或大型法阵类动画。
- 官方攻击、防御、异常、强化行动图标。

### 无慈第二日轮·终日态

定位：日耀回忆主终局 Boss。

它仍在执行白曜圣庭赋予它的职责：照亮、校准、礼拜、净化、保存秩序。它可怕的地方不是恶意，而是“已经坏掉的救赎系统仍在完整运行”。

机制方向：

| 相位 | 名称 | 效果 |
| --- | --- | --- |
| 1 | 晨祷 | 全体获得少量灼烧，Boss 获得护盾 |
| 2 | 正午 | 触发玩家灼烧，并攻击灼烧最高目标 |
| 3 | 净化 | 清除玩家一个负面状态，转化为焚身或名字压力 |
| 4 | 终日 | 根据保存/焚毁名字结算高压行动 |

名册参与：

- `SavedNames` 可以抵挡一次致命伤害或终日高压。
- `BurnedNames` 可以降低即时压力，但会把结局推向 `witch`。
- `NamelessNames` 不再提供保护，只提高 `white_city` 倾向。

击破后：

- 设置 `SolarFinaleSecondSunDefeatedKey = 1`。
- 不直接结算。
- 进入 `Sub_solar_finale_saint_gate` 做隐藏 Boss 判断。

占位资源：

- 官方大型 Boss、灾厄核心、太阳/光辉感较强的动画。
- 官方核心、攻击、强化、异常图标。

### 白曜圣女·乌娜

定位：隐藏剧情首领。

她不是魔女乌娜的邪恶面，也不是外部怪物。她是过去那个仍相信圣庭、第二日轮、净化和秩序的乌娜。她温柔、正确、洁净，但她的“净化”会连痛苦背后的名字和记忆一起抹除。

触发条件建议：

```text
SolarFinaleSecondSunDefeatedKey == 1
SavedNames >= SolarFinaleHiddenBossNameThreshold
BurnedNames < SolarFinaleHiddenBossNameThreshold
```

机制方向：

| 机制 | 说明 |
| --- | --- |
| 圣女净化 | 每回合清除自身部分灼烧 |
| 写回圣庭 | 将保存的名字转为无名名字 |
| 校准偏离 | 玩家日耀过高时，失去部分日耀并承受灼烧 |
| 无损礼装 | 清除自身灼烧并获得护盾 |
| 星名回响 | 若 SavedNames 达标，短暂禁止圣女净化并开放爆发窗口 |

战斗目标：

- 她不应该只是终日态换皮。
- 她要反向克制 SunExp 的舒适打法：净化灼烧、抹平火势、写回名字。
- 真正的解法不是更猛烈地烧，而是保留足够名字，在星名回响窗口让“被记住的人”压过白曜秩序。

击破后：

| 条件 | 结局 key |
| --- | --- |
| SavedNames 仍足够 | `stars` |
| BurnedNames 过高 | `witch` |
| 名字多数被写回/无名化 | `white_city` |

占位资源：

- 官方魔女、圣女、失心魔女或人形 Boss 动画。
- 官方净化、强化、防御、异常图标。

## 数据落点

### Map

修改：

```text
SunExp/Data/Map/sunexp.csv
SunExp/Text/Map/sunexp.csv
```

新增三条固定 Fight map。Text/Map 的 `Name` 用 Boss 中文名，`Note` 用 `首领`。

### Level

新增：

```text
SunExp/Data/Level/sunexp.csv
```

如果当前 mod 尚无 Level 表，则创建 SunExp 自己的 `sunexp.csv`。

建议 Level：

| Level ID | EnemyIds | Note | Level |
| --- | --- | --- | --- |
| `SunExp_sunexp_level_orbit_mirror_array` | `SunExp_sunexp_boss_orbit_mirror_array` | `boss` | `-1` |
| `SunExp_sunexp_level_second_sun_last_day` | `SunExp_sunexp_boss_second_sun_last_day` | `boss` | `-1` |
| `SunExp_sunexp_level_saint_wuna` | `SunExp_sunexp_boss_saint_wuna` | `boss` | `-1` |

### Enemy / EnemyCard / EnemyBless

新增：

```text
SunExp/Data/Enemy/sunexp.csv
SunExp/Text/Enemy/sunexp.csv
SunExp/Data/EnemyCard/sunexp.csv
SunExp/Text/EnemyCard/sunexp.csv
SunExp/Data/EnemyBless/sunexp.csv
SunExp/Text/EnemyBless/sunexp.csv
```

初版可以只做最小可运行战斗：

- 每个 Boss 3 到 4 张行动牌。
- 每张牌调用 `CS.SunExp.Dll.Scripting.BossScripts.*`。
- 复杂机制全部在 C# 中实现。
- CSV 只负责 ID、CD、目标、基础数值、脚本入口和官方资源占位。

## C# 落点

### `SolarMemoryMapNodePoolFactory.cs`

改动：

1. 新增固定剧情 Boss Map ID 常量或引用 `SunExpIds`。
2. 新增 `TryCreateFixedStoryBossNode(tree, layer, slot, out node)`。
3. 新增 `CreateExpandedBossPoolNode(tree)`。
4. 默认节点末尾优先使用固定剧情 Boss。
5. 其他首领节点使用全层首领池。
6. 保留 fallback 到 `tree.TypeGenerate("首领")`。

### `SolarMemoryModeRuntime.cs`

改动：

1. 替换当前第三层完成后直接 `GameExitUI` 的逻辑。
2. 增加终局路由状态：
   - `SolarFinaleSecondSunDefeatedKey`
   - `SolarFinaleSaintGateResolvedKey`
   - `SolarFinalePendingSaintBattleKey`
   - `SolarFinaleSaintDefeatedKey`
3. 终日态完成后进入 `Sub_solar_finale_saint_gate`。
4. 乌娜战斗完成后再进入 ending 或结算 UI。

### `EventScripts.cs`

新增入口：

```csharp
public static void InitSolarFinaleSaintGate(object self)
public static void EnterSolarFinaleSaintBattle()
public static void SkipSolarFinaleSaintBattle()
public static void ResolveSolarFinaleAfterSaintBattle()
```

事件脚本职责：

- 根据名字资源决定是否显示乌娜战斗选项。
- 玩家选择挑战时设置 pending battle。
- 玩家跳过时写入 ending key。
- 不直接硬加载任意 fight，优先交给地图运行时接管。

### `BossScripts.cs`

建议新增：

```text
SunExp-Dev/Scripting/BossScripts.cs
SunExp-Dev/Mechanics/SolarMemoryBossService.cs
```

`BossScripts` 是 CSV 调用入口；`SolarMemoryBossService` 存放实际机制。

初版入口：

```csharp
public static void OnFightStart(string bossId)
public static void OnRoundStart(string bossId)
public static void UseCard(string bossId, string cardId)
public static void OnDefeated(string bossId)
```

`OnDefeated` 需要处理：

- 终日态：设置 `SolarFinaleSecondSunDefeatedKey`。
- 乌娜：设置 `SolarFinaleSaintDefeatedKey` 和 ending key。

### `SunExpIds.cs`

新增 ID 常量：

```csharp
public const string SolarBossOrbitMirrorMapId = "solar_memory_boss_orbit_mirror_array";
public const string SolarBossSecondSunMapId = "solar_memory_boss_second_sun_last_day";
public const string SolarBossSaintWunaMapId = "solar_memory_boss_saint_wuna";

public const string SolarFinaleSaintGateResolvedKey = "SunExp_SolarFinaleSaintGateResolved";
public const string SolarFinalePendingSaintBattleKey = "SunExp_SolarFinalePendingSaintBattle";
public const string SolarFinaleSaintDefeatedKey = "SunExp_SolarFinaleSaintDefeated";
```

## 推荐实施阶段

### Phase 1：地图与池子

目标：先解决地图行为，不做完整 Boss 机制。

1. 新增三条固定 Map/Level，占位 Enemy 先使用官方资源模板或最小自定义 Enemy。
2. `SolarMemoryMapNodePoolFactory` 支持：
   - 第二层末尾固定镜阵。
   - 第三层末尾固定终日态。
   - 其他首领节点从全层首领池抽取。
3. 固定剧情 Boss 从全层首领池排除。

验收：

- 第二层末尾必定是 `白曜镜阵·三千环日镜`。
- 第三层末尾必定是 `无慈第二日轮·终日态`。
- 非固定首领节点能抽到不同层级的官方首领。
- 乌娜不会出现在随机首领节点。

### Phase 2：终局事件路由

目标：完成第三层后事件判断。

1. 新增 `Sub_solar_finale_saint_gate`。
2. 终日态完成后不直接结算，而是进入该事件。
3. 事件根据名字资源显示乌娜战斗选项。
4. 跳过或不满足条件时进入 ending。

验收：

- 第三层末尾完成后出现事件场景。
- 满足条件时能看到乌娜战斗选项。
- 不满足条件时不会出现乌娜战斗选项。
- 选择跳过能正常结算。

### Phase 3：乌娜固定战斗

目标：把事件选项接到真实 Fight。

1. `EnterSolarFinaleSaintBattle()` 设置 pending key。
2. 运行时生成固定乌娜战斗节点。
3. 进入战斗后清除 pending key。
4. 战斗胜利后设置 ending key。

验收：

- 乌娜只从事件选项进入。
- 乌娜不会污染普通首领池。
- 乌娜战斗后能回到终局事件或结算 UI。

### Phase 4：Boss 机制细化

目标：从可运行占位升级为完整玩法。

1. 镜阵实现折射和校准。
2. 终日态实现四相循环和名册挡刀。
3. 乌娜实现净化、写回圣庭、星名回响。
4. 修正所有旧世界观文案。

验收：

- 三个 Boss 玩法互相区分。
- 名字资源不只是结局分数，而会影响终局战。
- `stars` / `white_city` / `witch` 三结局都可到达。

## 风险与处理

| 风险 | 说明 | 处理 |
| --- | --- | --- |
| 事件直接进 Fight 的同步风险 | 反编译显示 EventUI 主要负责事件换图，不是任意战斗加载器 | 用 pending key + 地图运行时固定节点接管 |
| 全层首领池抽到异常/测试 Map | 官方表中可能存在禁用或特殊行 | 排除 `*`、空 NodeId、非 Fight、非 boss Level |
| Level 差异导致数值不稳 | 跨层抽 Boss 会让低层/高层 Boss 进入日耀回忆 | 初版接受多样性，后续可加权或黑名单 |
| 固定 Boss 被随机池抽到 | 三个剧情 Boss 不能出现在普通池 | `IsSolarMemoryFixedStoryBoss` 强排除 |
| `ExLockDes` 改变末尾槽 | 固定 index 会错位 | 按默认段长度动态取最后槽 |

## 验证清单

实现后运行：

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-event-dev\scripts\validate-sunexp-events.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

建议新增或手动验证：

- `SolarMemoryMapNodePoolFactory` 主路径不再裸用 `tree.TypeGenerate("首领")`。
- 第二层末尾节点 Map ID 固定为 `solar_memory_boss_orbit_mirror_array`。
- 第三层末尾节点 Map ID 固定为 `solar_memory_boss_second_sun_last_day`。
- 扩展首领池排除三个 SunExp 固定剧情 Boss。
- `Sub_solar_finale_saint_gate` 是 `Sub_` 事件，不进入普通随机事件池。
- 终局文案不再出现“奥尔德林不再拥有太阳”。

## 下一步建议

先做 Phase 1 和 Phase 2。原因是这两个阶段能最快验证路线是否成立：地图节点是否按预期出现、全层首领池是否扩展、第三层后是否能进入事件判断。

Boss 机制可以在路线打通后再细化，否则很容易把时间消耗在 EnemyCard 和 Bless 调参上，却还没有验证终局路由是否可用。
