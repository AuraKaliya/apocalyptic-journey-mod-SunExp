# SolarMemory 牌组隔离边界拆分（第二十七轮开发记录）

> 日期：2026-07-18
>
> 前置未提交批次：第二十三至第二十六轮 SolarMemory 拆分
>
> 范围：CardPackCheck、卡包选择、事件卡分类、角色牌组清洗、备用池与原生牌组窗口

## 1. 本轮结论

本轮完成 `SolarMemoryDeckIsolationRuntime` 拆分。`SolarMemoryModeRuntime` 不再拥有 CardPackCheck Hook、卡包回退规则、事件卡分类、角色牌组清洗或 OutDeckUI 入口。

新边界负责：

- `GameConfigManager.CardPackCheck` Before 过滤；
- 初始模式启动卡包选择；
- 当前玩家卡包选择与旧单机存档回退；
- 在线专属卡包兼容检查；
- 根据 id、Type、Note、本地化类型和脚本标记识别事件卡；
- 清理角色 active deck 与 `UnCardList`；
- 清空备用池并同步角色计数；
- 标记当前玩家 deck configured；
- 整备完成前恢复 StarterDeck 流程，完成后打开原生 OutDeckUI。

## 2. CardPackCheck 时序

事件卡过滤仍在 `GameConfigManager.CardPackCheck` Before 执行。只有当前运行是 SolarMemory 且第一个参数是宿主候选列表时才原地移除事件卡。

没有修改宿主 CardPackCheck 的返回值、锁定检查或其他模式候选列表。Hook 所有者从主 Runtime 改为 `SolarMemoryDeckIsolation`，便于诊断职责归属。

## 3. 卡包选择优先级

当前卡包选择顺序保持不变：

1. `SolarMemoryPlayerSetupState.SelectedPacks()` 中的玩家范围选择；
2. 仅单机读取 `SolarMemorySelectedPacksKey` 旧存档值；
3. 宿主 `GameRuntimeData.UseCardPack`；
4. 前六个未锁定可见卡包。

联机不会读取或迁移旧全局选择，避免一名玩家的卡包状态被分配给其他玩家。`cardpack_13` 仍通过 `GameCompatibilityApi.ShouldEnableOnlineCardPack` 判断当前大厅是否可用。

## 4. 事件卡分类与只读配置

分类规则保持原样，覆盖：

- `solar_memory_event`、`SolarMemoryEvent`、`event_`、`card_event` 和 `_event_` id 标记；
- `Event`、事件、事件牌、事件卡和 `EventCard` 类型；
- DataConfig 的 Type、Note、Tag、Action、InitScript、UseScript；
- 本地化 Type/Note。

运行时只读取 `DataConfig.data`，没有写入宿主只读配置字典。列表删除只作用于 CardPackCheck 候选副本、角色 `cardList` 与 `UnCardList`。

## 5. StarterDeck 与 GameApi 调用方

以下调用方已直接切换到新边界：

- `SolarMemoryModeEntryRuntime`：初始卡包；
- `SolarMemoryStarterDeckRuntime`：当前卡包、候选过滤、三条最终牌组清洗路径和备用池清空；
- `SolarMemoryFlowApi.OpenDeckWindow`：原生牌组窗口入口。

没有保留主 Runtime 代理方法，因此后续文件移动不会再次依赖固定主文件源码字符串。

## 6. 原生牌组窗口与角色状态

`OpenDeckWindow` 继续要求 deck configured 和 starter deck applied 同时成立。未完成时恢复 `SolarMemoryPreparationRuntime.StartOrResume`，不会提前清空备用池或标记已配置。

打开 OutDeckUI 前再次清理 active/reserve 事件卡。清空备用池后继续更新 `CardTopCount`、`CardBottomCount`、`MaxAlCardCount`，并只在传入角色是本地 `RoleTable.Instance` 时更新玩家范围准备标记。

## 7. 主 Runtime 收缩与护栏

`SolarMemoryModeRuntime.cs` 从第二十六轮的 782 行降至 479 行；新牌组隔离 Runtime 为 334 行。

新增门禁要求：

- 新边界文件必须存在并由主 Runtime 初始化；
- CardPackCheck、卡包索引、事件卡分类和 OutDeckUI 入口必须位于新边界；
- 主 Runtime 禁止重新引入 CardPackCheck、牌组清洗或 OpenDeckWindow；
- StarterDeck 必须直接调用新边界；
- 模式入口必须通过新边界取得初始卡包；
- 角色 active/reserve 列表必须同时清洗；
- 联机禁止读取旧全局卡包选择；
- 未完成 StarterDeck 时必须恢复准备流程。

## 8. 兼容性与后续方向

本轮没有修改 StarterDeck Arbiter 协议、最终角色提交、地图、Boss、结算、EventList、Card 数据、共享协议或多人 RPC。

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

下一轮建议评审剩余约 479 行主 Runtime：优先拆分地图生成/同步协调与 currentNode 恢复，或在边界已足够稳定时停止细拆并转入 SolarMemory 目录整理与文件命名收口。
