# SolarMemory 地图生命周期边界拆分（第二十八轮开发记录）

> 日期：2026-07-18
>
> 前置未提交批次：第二十三至第二十七轮 SolarMemory 拆分
>
> 范围：地图生成、同步数组修复、`currentNode` 恢复、固定槽重投影与主 Runtime 收口

## 1. 本轮结论

审查剩余 479 行 `SolarMemoryModeRuntime` 后，确认其中绝大部分仍属于同一组地图生命周期职责，而不是需要继续逐方法细拆的零散逻辑。因此本轮新增 `SolarMemoryMapLifecycleCoordinator`，整体接管地图 Hook、地图状态修复和客户端节点恢复；主 Runtime 收口为组合入口。

拆分后的职责为：

- `SolarMemoryModeRuntime`：初始化各子边界、提供整备窗口兼容入口、查询当前是否为日耀回忆运行；
- `SolarMemoryMapLifecycleCoordinator`：注册地图生成与同步 Hook，协调节点池应用、数组修复、客户端 `currentNode` 恢复、存档节点更新和固定槽重投影；
- `SolarMemoryMapVisualRuntime`：只注册地图表现 Hook；
- `SolarMemoryMapProjectionRuntime`：只执行 Unity 地图对象、标题和纹理投影。

## 2. Hook 所有权

以下 Hook 从主 Runtime 迁入地图生命周期协调器，注册时序和 Before/After 方向保持不变：

- `NormalMapManager.RandomGenerate` Before；
- `NormalMapManager.GeneratrMap` After；
- `MapSelectUI.ReadyToSelect` Before；
- `MapManager.CmdSelectMap` 及带 sender/生成名兼容入口 Before；
- `MapManager.TargetUpdateMap`、`RpcUpdateMap` Before；
- `MapManager.RpcNextMap` Before/After。

Hook 日志所有者由笼统的 `SolarMemoryMode` 改为 `SolarMemoryMapLifecycle`，便于按边界诊断注册失败。

## 3. 地图同步与恢复契约

协调器继续复用既有 Mechanics 边界，没有复制纯规则：

- 当前层节点池继续委派给 `SolarMemoryMapNodePoolApplier.ApplyToCurrentLayer`；
- 固定槽数组继续委派给 `SolarMemoryMapSyncRepairService.Repair`；
- 同步数组恢复出的每个节点继续补齐确定性 `NodeDice`；
- 客户端恢复后继续写回 `MapTree.currentNode` 和 `GameSaveManager.UpdateNode`；
- `ShowMap` 只有在当前节点可用或可从同步数组恢复时才重投影固定槽；
- 待执行的圣女乌娜 Boss 转场仍在 `ReadyToSelect` 和 `ShowMap` 两个稳定边界重试。

## 4. 调用方收口

直接依赖地图辅助能力的调用方已改为面向新协调器：

- `SolarMemoryBattleExitCoordinator` 复用节点可用性、骰子修复、地图状态应用和同步数组恢复；
- `SolarMemoryBossTransitionCoordinator` 复用客户端判定和固定槽数组修复；
- `SolarMemoryMapProjectionRuntime` 复用客户端判定；
- `SolarMemoryMapVisualRuntime` 将 `ShowMap` 回调交给地图生命周期协调器。

`SolarMemoryModeRuntime.IsSolarMemoryRun()` 仍作为模式级查询保留，避免把通用运行状态复制到每个协调器。

## 5. 主 Runtime 收口

`SolarMemoryModeRuntime.cs` 从 479 行降至 47 行；新协调器为 443 行。主 Runtime 只保留：

- 七个子运行时/协调器的初始化顺序；
- `OpenOriginWindow`、`OpenBlessingWindow` 兼容入口；
- 统一的整备恢复异常边界；
- `IsSolarMemoryRun` 模式状态查询。

主 Runtime 不再引用 `MapManager`、不再注册 Hook，也不再持有地图行解析、同步节点构造或 Unity 投影编排。

## 6. 架构护栏

架构与源码检查已调整为读取 `SolarMemoryMapLifecycleCoordinator.cs`，并新增以下约束：

- 新文件必须存在且由主 Runtime 初始化；
- 地图生成和 `ReadyToSelect` Hook 必须位于新协调器；
- 同步数组修复必须继续委派给 Mechanics；
- 地图行必须通过 `TerriasConfigIndex` 读取；
- 战斗退出和 Boss 转场必须直接复用新协调器；
- `ShowMap` 表现 Hook 必须调用新协调器；
- 主 Runtime 禁止重新出现 `RegisterBefore` 或 `MapManager`；
- 主 Runtime 继续禁止直接依赖 Unity、固定槽视觉状态和各已拆出的战斗/结算/牌组实现。

## 7. 后续方向

主 Runtime 已达到“只负责初始化、编排和委派”的目标。SolarMemory 后续不建议继续为了行数机械拆分；下一步应先整理当前二十三至二十八轮形成的文件命名和文档索引，再进入 DamageMeter、StarterDeck 或 ConfigModels 的下一复杂模块批次。

## 8. 验证结果

本轮已通过：

- Terrias Release 构建：0 警告、0 错误；
- Terrias 架构断言；
- Terrias C#：312 项行为断言与源码护栏；
- SolarMemory/Event 校验：6 个事件、10 个地图行、0 警告；
- Terrias 全量内容校验：56 张卡牌、13 个遗物、33 个 Buff、5 个卡包、3 个敌人、0 警告；
- Terrias、SanGuoShaExp、AuraToolsExp 三个共享消费者 Release 构建：均为 0 警告、0 错误；
- Aura.Shared：1228 项公共 API 兼容基线；
- Aura.Shared DLL 打包一致性检查；
- `git diff --check`（仅报告仓库既有 CRLF 转换提示，无空白错误）。

共享运行时构建产物和三个消费者副本的 SHA-256 均保持为 `A45F9B72230318FC380B020F6BE2A8E30D26F0F681CFF51565D940CE26CFFD50`。
