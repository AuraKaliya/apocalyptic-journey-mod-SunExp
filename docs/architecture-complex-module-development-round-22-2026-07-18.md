# SolarMemory 固定节点与同步修复边界拆分（第二十二轮开发记录）

> 日期：2026-07-18
>
> 前置提交：`6030820b`（拆分 AuraTools 配置模型并完善兼容护栏）
>
> 范围：固定节点规格、日耀地图同步数组修复、非日耀内容隔离数组策略，以及对应纯行为测试

## 1. 本轮结论

本轮完成 SolarMemory 复杂 Runtime 的第一轮职责迁移。`SolarMemoryModeRuntime` 不再拥有固定节点规格缓存和同步数组逐槽改写逻辑；`SolarMemoryContentIsolationRuntime` 不再直接实现同步数组遍历和替换规则。

新增三个 Mechanics 边界：

- `SolarMemoryFixedNodeCatalog`：三层固定槽、事件映射和 Boss 规格的唯一来源；
- `SolarMemoryMapSyncRepairService`：根据固定节点目录修复 `maps`/`mapData`，并处理错位的日耀专属节点；
- `SolarMemoryContentIsolationService`：识别非日耀同步数组中的专属内容，只接受完整且非专属的回退结果。

Hook Runtime 继续负责宿主对象读取、Hook 时序、配置表回退解析、`MapTree`/currentNode、Unity 表现和诊断日志。

## 2. 固定节点规格统一

固定槽不再由 `SolarMemoryModeRuntime` 内部的嵌套类型和可变缓存构造。目录现在集中声明：

- 第一层：槽 0、槽 5 为剧情；
- 第二层：槽 0、槽 3 为剧情，槽 5 为白曜镜阵；
- 第三层：槽 0、槽 3 为剧情，槽 4 为无慈第二日轮，槽 5 为圣者乌娜。

`SolarMemoryMapNodePoolFactory` 的槽位常量、层归一化和事件索引也委派给该目录，避免地图生成与同步修复各自维护一套数字和映射。

## 3. 日耀同步数组修复

`SolarMemoryMapSyncRepairService` 是不依赖 Unity、Witch 类型和 Hook 上下文的纯数组服务。它负责：

1. 将当前层全部固定槽恢复到期望 Map/Event/Level id；
2. 检测固定槽之外误入的日耀专属 Map/Event；
3. 按同层、同槽事件映射确定性替换错位节点；
4. 只处理 `maps` 和 `mapData` 的共同长度；
5. 对已经正确的数组保持幂等；
6. 通过回调把修复明细交还 Runtime 记录日志。

`SolarMemoryModeRuntime` 现在只解析当前层、调用服务，并继续执行 currentNode、保存节点和 NodeDice 的宿主对象修复。

## 4. 非日耀内容隔离

`SolarMemoryContentIsolationService` 集中执行同步选择数组的专属内容识别与安全替换。它依赖 `TerriasIds.IsSolarMemoryExclusiveMapId` 和 `IsSolarMemoryExclusiveEventId`，不会复制专属 id 规则。

Runtime 提供配置表相关回退解析；纯服务会拒绝：

- 空 Map id；
- 空 Node id；
- 仍属于 SolarMemory 的 Map/Event 回退结果。

生成树、currentNode、NodeDice 和 `GameSaveManager.UpdateNode` 仍留在 Hook/宿主对象边界。

## 5. Runtime 收缩

删除了已经没有调用者的旧地图重写私有路径：

- `RewriteSolarMemoryDefaultLayer`；
- `RewriteSolarMemorySelectLayer`；
- `CreateSolarMemoryEventNode`；
- `CreateBossChainNode`；
- 对应的段长度和 Break 节点辅助方法。

当前地图池仍只由 `SolarMemoryMapNodePoolFactory` 和 `SolarMemoryMapNodePoolApplier` 生成与应用，不存在第二套 Runtime 内部生成路径。

`SolarMemoryModeRuntime.cs` 从 1913 行降至 1652 行。本轮没有移动 Boss 对话、战斗退出、最终结算或 Unity 固定槽表现逻辑，这些属于后续独立边界。

## 6. 测试和架构护栏

Terrias net8 纯测试新增覆盖：

- 三层固定节点目录及越界层归一化；
- 最终层四个固定槽和错位专属节点修复；
- 普通槽保持不变；
- 修复幂等；
- `maps`/`mapData` 长度不一致；
- 非日耀数组仅解析专属项；
- 安全回退应用与专属回退拒绝。

架构门禁新增约束：

- 三项规则必须位于 Mechanics；
- 纯规则禁止依赖 Witch/Unity 宿主类型；
- Runtime 必须委派同步数组修复；
- Runtime 禁止重新拥有嵌套固定节点规格、逐槽修复方法或旧地图生成路径；
- ContentIsolation Runtime 必须委派同步数组变更。

Terrias C# 行为断言由 282 项增加到 312 项。

## 7. 兼容性

本轮没有修改：

- Hook 目标、Before/After 时序和注册顺序；
- 三层固定槽布局、Map id、Event id 或 Boss Level id；
- `MapTree`、currentNode、NodeDice 和保存节点修复时机；
- Boss/结局路由、战斗退出和第三层原生结算；
- 整备流程、多人角色提交或 RPC 协议；
- EventList、Map、文本和视觉资源数据。

## 8. 完整验证结果

本轮已通过：

- Terrias Release 构建：0 警告、0 错误；
- Terrias 架构断言；
- Terrias C#：312 项行为断言及源码护栏；
- SolarMemory/Event 校验：6 个事件、10 个地图行、0 警告；
- Terrias 全量内容校验：56 张卡牌、13 个遗物、33 个 Buff、5 个卡包、3 个敌人、0 警告；
- 三个主消费者 Release 构建：均为 0 警告、0 错误；
- Aura.Shared：1228 项公共 API 兼容基线；
- Aura.Shared DLL 打包一致性检查。

最终构建产物和三个打包副本的 SHA-256 均为 `52F58AFE34BDB883EAA274880EA9C3685A51A52FDB1E9E0DB3665B5256C61197`。

## 9. 后续方向

下一轮 SolarMemory 可继续拆分 `SolarMemoryMapProjectionRuntime`，迁移固定槽 Unity 对象创建、地图卡纹理和层标题；随后再分别处理 BattleExit、BossTransition 和 Settlement 协调器。每一轮都应保持 Hook 时序与第三层直接结算契约不变。
