# SolarMemory 退出战斗协调边界拆分（第二十四轮开发记录）

> 日期：2026-07-18
>
> 前置未提交批次：第二十三轮地图投影边界拆分
>
> 范围：逃跑、战败、临时 UI 清理、transition currentNode 修复

## 1. 本轮结论

本轮完成 `SolarMemoryBattleExitCoordinator` 拆分。`SolarMemoryModeRuntime` 不再直接注册或实现逃跑、战败 Hook，也不再保存逃跑嵌套状态和直接调用 `SunExpUiSafety` 销毁整备窗口。

新边界负责：

- `Fight_Escape.ResetStates` Before：标记退出过程、清除隐藏 Boss pending、修复 currentNode、关闭临时 UI；
- `Fight_Escape.ResetStates` After：再次修复 currentNode、关闭宿主重置后可能残留的临时 UI、释放退出过程标记；
- `Fight_Loss.Init` After：清除失败分支的临时状态，并在该失败不是逃跑嵌套调用时单独修复 currentNode；
- 为 Saint Wuna Boss 转场复用同一个临时 UI 清理入口。

## 2. Hook 时序保持

原有三条 Hook 的 Before/After 关系保持不变：

1. `Fight_Escape.ResetStates` Before 仍先于宿主 `MapManager.TryChange` 消费 currentNode；
2. `Fight_Escape.ResetStates` After 仍处理宿主重置后的地图状态和 UI 残留；
3. `Fight_Loss.Init` After 仍覆盖宿主假失败/逃跑组合路径。

协调器从 `SolarMemoryModeRuntime.Initialize` 原三条 Hook 所在的位置初始化，因此相对 `Fight_Win.ResetStates` 和最终层结算 Hook 的注册顺序没有改变。Hook 目标改用 `SunExpHookTargets.FightEscapeResetStates` 与 `SunExpHookTargets.FightLossInit` 常量。

## 3. currentNode 恢复策略

退出战斗时的恢复顺序保持原样：

1. 当前 `MapTree.currentNode` 可用时补齐 `NodeDice` 并写入存档；
2. 当前节点不可用时尝试从 `GameSaveManager` 恢复；
3. 仍不可用时复用既有同步数组恢复路径；
4. 最后通过 `SolarMemoryMapNodePoolApplier` 重建当前层并再次校验节点。

本轮没有复制同步数组构建逻辑。协调器调用主 Runtime 已有的内部恢复协作点，从而保持地图选择、ShowMap、RpcNextMap 与退出战斗共用同一套节点链构建规则。

## 4. UI 清理边界

`SolarMemoryBattleExitCoordinator.CloseTransientUi` 统一执行：

- `SolarMemorySetupFlowRuntime.ClosePreparationWindows`；
- `SolarMemoryBlessingPickerRuntime.Close`；
- 通过 `SunExpUiSafety.DisableRaycastsAndDestroyByName` 清理旧牌包窗口、StarterDeck、OriginSetup、BlessingSetup 和 BlessingPicker 根节点。

所有清理继续先关闭射线再销毁对象，并保留 `SunExpUiSafety` 的 Graphic registry 延迟清扫。没有修改 ModalHost、UI 池、Sprite 缓存或窗口结构。

## 5. 主 Runtime 收缩

`SolarMemoryModeRuntime.cs` 从第二十三轮的 1266 行进一步降至 1125 行；新协调器为 172 行。主 Runtime 现在只在初始化阶段委派退出战斗协调器，并为地图恢复与隐藏 Boss pending 提供内部协作点。

主 Runtime 不再包含：

- `handlingSolarMemoryFightAbort`；
- `PrepareSolarMemoryFightAbort`；
- `SettleSolarMemoryFightAbort`；
- `SettleSolarMemoryFightLoss`；
- `EnsureSolarMemoryCurrentNodeForTransition`；
- `CloseSolarMemoryTransientUi`；
- 对 `SunExp.Dll.Hooks.Ui` 的直接依赖。

## 6. 架构护栏

架构和源码测试新增以下约束：

- 新协调器文件必须存在；
- 主 Runtime 必须初始化协调器；
- 三条退出 Hook 必须由协调器通过集中目标常量拥有；
- 逃跑前后必须执行 currentNode 修复；
- StarterDeck 清理必须继续通过 `SunExpUiSafety`；
- 主 Runtime 禁止重新引入退出战斗处理器、退出状态和直接 UI 安全销毁逻辑。

## 7. 兼容性

本轮没有修改：

- Boss 胜利判断、第二日轮分支和 Saint Wuna 战斗启动；
- 第三层结算或旧等级 30 存档迁移；
- 地图数组格式、固定槽、NodeDice 规则或多人权威模型；
- EventList、Map、Level、Enemy、Dialogue 数据；
- VisualRegistry、VisualBundle、纹理资源或共享协议。

## 8. 验证与后续方向

本轮已通过：

- SunExp Release 构建：0 警告、0 错误；
- SunExp 架构断言；
- SunExp C#：312 项行为断言和源码护栏；
- SolarMemory/Event 校验：6 个事件、10 个地图行、0 警告；
- SunExp 全量内容校验：56 张卡牌、13 个遗物、33 个 Buff、5 个卡包、3 个敌人、0 警告；
- SunExp、SanGuoShaExp、AuraToolsExp 三个消费者 Release 构建：均为 0 警告、0 错误；
- Aura.Shared：1228 项公共 API 兼容基线；
- Aura.Shared DLL 打包一致性检查。

最终构建产物和三个消费者副本的 SHA-256 均为 `A45F9B72230318FC380B020F6BE2A8E30D26F0F681CFF51565D940CE26CFFD50`。

SolarMemory 地图投影和异常退出协调边界现已形成。下一轮建议拆分 `SolarMemoryBossTransitionCoordinator`，迁移第二日轮胜利分支、剧情 pending、Saint Wuna 节点构建/同步和转场 UI；最终结算仍应保留为其后的独立批次。
