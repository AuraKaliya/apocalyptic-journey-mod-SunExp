# SolarMemory Boss 转场协调边界拆分（第二十五轮开发记录）

> 日期：2026-07-18
>
> 前置未提交批次：第二十三轮地图投影、第二十四轮退出战斗协调
>
> 范围：Boss 胜利分支、剧情 pending、Saint Wuna 节点构建/同步和转场 UI

## 1. 本轮结论

本轮完成 `SolarMemoryBossTransitionCoordinator` 拆分。`SolarMemoryModeRuntime` 不再直接拥有 `Fight_Win.ResetStates` Hook、Boss 剧情状态或 Saint Wuna 地图转场实现。

新协调器负责：

- 第二日轮胜利后的有钥匙卡/无钥匙卡分支；
- 第二日轮结局、Saint Wuna 前奏和隐藏结局对话启动；
- 对话完成后的托管回调；
- settlement pending 和 Saint Wuna transition pending 状态；
- Saint Wuna 固定 Boss 节点、终止子节点和 `NodeDice`；
- `MapTree.currentNode`、存档、`mapList/mapData` 终点槽同步；
- 转场前对话、战斗、奖励、地图和整备临时 UI 清理；
- 通过 `MapManager.CmdNextMap` 进入隐藏 Boss。

## 2. Hook 与剧情时序

`Fight_Win.ResetStates` 仍使用 After Hook，并在 `SolarMemoryModeRuntime.Initialize` 原有胜利 Hook 所在位置初始化协调器。目标名称改用 `TerriasHookTargets.FightWinResetStates`。

分支顺序保持不变：

1. 第二日轮胜利并持有 `炽冕崩落`：关闭 FightUI，启动 Saint Wuna 前奏；
2. 前奏完成：先写入可重试 pending，再尝试主机转场；
3. 第二日轮胜利但没有钥匙卡：启动普通结局对话，完成后进入结算；
4. Saint Wuna 胜利：启动隐藏结局对话，完成后进入结算；
5. 对话启动失败：回退到原有立即转场或立即结算路径。

## 3. pending 状态归属

`solarMemoryStorySettlementPending` 与 `solarMemorySaintWunaBossTransitioning` 已迁入 Boss 协调器。

主 Runtime 的 `ReadyToChangeMap` 和旧等级 `MapItemInit` 结算门禁通过 `SolarMemoryBossTransitionCoordinator.IsSettlementPending` 读取只读状态，因此对话期间不会提前结算。地图 `ReadyToSelect/ShowMap` 仍会触发协调器的 pending 重试，以覆盖前奏完成时地图对象尚未恢复的情况。

逃跑和战败由 `SolarMemoryBattleExitCoordinator` 调用 Boss 协调器清除隐藏 Boss pending，状态所有权不再回流主 Runtime。

## 4. Saint Wuna 节点与同步

转场节点仍通过 `SolarMemoryMapNodePoolFactory.CreateFixedBossNode` 创建，并保留：

- `Id = SolarBossSaintWunaMapId`；
- `Type = Fight`；
- `NodeId = SolarBossSaintWunaLevelId`；
- `Level = -1`；
- 通过 `MapNodeSafetyService.EnsureNodeDice` 补齐 Boss 节点和终止子节点；
- 在调用 `CmdNextMap` 前写入 `MapTree.currentNode` 和 `GameSaveManager`；
- 复用主 Runtime 已有的同步数组修复，再覆盖终点槽为 Saint Wuna Map/Level id。

没有创建额外地图层，也没有改变主机权威和客户端只等待主机推进的规则。

## 5. GameApi 与 UI 边界

`SolarMemoryFlowApi` 的三条剧情完成回调现在委派给 `SolarMemoryBossTransitionCoordinator`，不再回到主 Runtime。CSV/EventScripts 入口没有变化，仍只访问 GameApi facade。

转场 UI 清理继续复用 `SolarMemoryBattleExitCoordinator.CloseTransientUi` 处理整备窗口，然后关闭 DialogueUI、FightUI、BattleRewardsUI 和 MapSelectUI。没有新增视觉资源、Overlay、射线节点或同步资源加载。

## 6. 主 Runtime 收缩

`SolarMemoryModeRuntime.cs` 从第二十四轮的 1125 行降至 866 行；新 Boss 协调器为 293 行。

主 Runtime 不再包含：

- `SettleSolarMemoryBossAfterWin`；
- `TryStartSolarMemoryBossDialogue`；
- 两个 Boss 剧情完成公开方法；
- `TryContinuePendingSaintWunaBoss`；
- Saint Wuna 节点和终止节点构建；
- Boss 同步数组覆盖和转场 UI 清理；
- `solarMemoryStorySettlementPending`；
- `solarMemorySaintWunaBossTransitioning`；
- 钥匙卡判定的短 id 辅助逻辑。

最终层级推进、`GameExitUI` 展示和旧等级存档结算仍留在主 Runtime，作为下一批独立拆分范围。

## 7. 架构护栏

新增门禁要求：

- Boss 协调器文件必须存在并由主 Runtime 初始化；
- 胜利 Hook 必须由协调器通过集中 Hook 目标拥有；
- 主 Runtime 禁止重新引入 Boss 胜利处理器、剧情 pending 或 Saint Wuna 节点构建；
- 主 Runtime 的两个结算入口必须读取协调器 pending；
- Boss 节点必须通过节点工厂创建并使用 `MapNodeSafetyService`；
- 转场前必须持久化 currentNode、修复同步数组、关闭奖励 UI 并调用 `CmdNextMap`；
- `SolarMemoryFlowApi` 必须把前奏完成回调委派给 Boss 协调器。

## 8. 兼容性与后续方向

本轮没有修改 EventList、Map、Level、Enemy、Dialogue、VisualRegistry、VisualBundle、共享协议或多人 RPC。

本轮已通过：

- Terrias Release 构建：0 警告、0 错误；
- Terrias 架构断言；
- Terrias C#：312 项行为断言和源码护栏；
- SolarMemory/Event 校验：6 个事件、10 个地图行、0 警告；
- Terrias 全量内容校验：56 张卡牌、13 个遗物、33 个 Buff、5 个卡包、3 个敌人、0 警告；
- Terrias、SanGuoShaExp、AuraToolsExp 三个消费者 Release 构建：均为 0 警告、0 错误；
- Aura.Shared：1228 项公共 API 兼容基线；
- Aura.Shared DLL 打包一致性检查。

最终构建产物和三个消费者副本的 SHA-256 均为 `A45F9B72230318FC380B020F6BE2A8E30D26F0F681CFF51565D940CE26CFFD50`。

下一轮建议拆分 `SolarMemorySettlementCoordinator`，迁移第三层完成、旧等级终章存档恢复、Level 32 路由和 `SolarMemorySettlementPresenter` 调用；牌组与事件卡清理应继续作为其后的独立批次。
