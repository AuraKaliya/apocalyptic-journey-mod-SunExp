# SolarMemory 结算协调边界拆分（第二十六轮开发记录）

> 日期：2026-07-18
>
> 前置未提交批次：第二十三至第二十五轮 SolarMemory 拆分
>
> 范围：第三层完成、旧等级终章存档恢复、Level 32 路由和结算 Presenter 调用

## 1. 本轮结论

本轮完成 `SolarMemorySettlementCoordinator` 拆分。`SolarMemoryModeRuntime` 不再注册结算 Hook，不再拥有旧终章等级常量、层级推进方法或 `GameExitUI` 展示入口。

新协调器负责：

- `NormalMapManager.ReadyToChangeMap` Before 的第三层完成检测；
- `NormalMapManager.MapItemInit` Before 的旧等级终章存档恢复；
- Boss 对话 pending 期间延迟结算；
- 将当前 `NormalMapManager.Level` 路由到原生结算等级；
- Boss 剧情完成后的立即结算入口；
- 显式 GameApi 结算展示入口；
- 委派 `SolarMemorySettlementPresenter` 打开宿主 `GameExitUI`。

## 2. Hook 时序保持

两个 Hook 仍保持 Before 时序：

1. `NormalMapManager.MapItemInit` Before：旧存档在宿主按过期地图数组创建 MapItem 前完成结算；
2. `NormalMapManager.ReadyToChangeMap` Before：第三层完成后先把 Level 提升到 32，再交给宿主原生切图/结算逻辑。

协调器从 `SolarMemoryModeRuntime.Initialize` 原结算 Hook 所在阶段初始化。没有在 `MapItemInit` Before 重写 MapTree，也没有新建终章地图层。

## 3. 当前与旧存档结算路径

当前流程：

- `manager.Level < SolarMemoryMaxLayer * 6` 时不处理；
- 达到第三层终点后设置 `Level = 32`；
- 宿主 `ReadyToChangeMap` 继续进入原生成功结算。

旧存档流程：

- `manager.Level < 30` 时不处理；
- 等级 30 以上且没有 Boss 结局对话 pending 时，先归一化到 `SolarMemoryMaxLayer * 6`；
- 立即调用 Presenter 打开当前结算 UI，避免宿主使用旧终章地图结构。

两个流程都继续读取 `SolarMemoryBossTransitionCoordinator.IsSettlementPending`，确保结局对话未完成时不会提前结算。

## 4. Boss 与 GameApi 委派

`SolarMemoryBossTransitionCoordinator` 的以下路径已改为委派结算协调器：

- 第二日轮无钥匙卡的对话失败回退；
- Saint Wuna 结局对话失败回退；
- 两类结局对话完成回调。

`SolarMemoryFlowApi.ShowSettlement` 也直接调用 `SolarMemorySettlementCoordinator.ShowSolarMemorySettlement`。CSV/EventScripts 边界没有变化。

## 5. Presenter 与 UI

`SolarMemorySettlementPresenter` 继续独立负责：

- 关闭 MapSelectUI 和 EventUI；
- 设置 `GameExitUI.loss = false`；
- 通过 `UIManager.ShowUI<GameExitUI>` 打开宿主成功结算。

本轮没有复制或修改 Presenter 内部 UI 逻辑，也没有新增视觉资源、缓存、Overlay 或射线表面。

## 6. 主 Runtime 收缩

`SolarMemoryModeRuntime.cs` 从第二十五轮的 866 行降至 782 行；新结算协调器为 112 行。

主 Runtime 不再包含：

- `LegacySolarFinaleMapLevel`；
- `FinishSolarMemoryAfterFinalLayer`；
- `SettleLegacyTerminalLevelBeforeMapItems`；
- `CompleteSolarMemoryRun`；
- `CompleteSolarMemoryRunForSettlement`；
- `ShowSolarMemorySettlement`；
- 对 `SolarMemorySettlementPresenter` 的直接调用。

## 7. 架构护栏

新增门禁要求：

- 结算协调器文件必须存在并由主 Runtime 初始化；
- 两个结算 Hook 必须由结算协调器拥有；
- 主 Runtime 禁止重新引入结算处理器和 Presenter 调用；
- 旧存档阈值必须保持 30；
- 当前第三层必须路由到原生等级 32；
- 两条结算路径必须读取 Boss 对话 pending；
- Boss 协调器和 GameApi 必须委派结算协调器；
- Presenter 继续独占 `GameExitUI` 创建。

## 8. 兼容性与后续方向

本轮没有修改地图数组、固定节点、Boss 分支、EventList、Map、Level、Dialogue、VisualRegistry、VisualBundle、共享协议或多人 RPC。

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

下一轮建议拆分牌组和事件卡隔离边界，例如 `SolarMemoryDeckIsolationRuntime`，迁移 CardPackCheck 过滤、可见卡包选择、事件卡识别、角色牌组清洗和备用池清理；地图生成与同步协调继续保持现状。
