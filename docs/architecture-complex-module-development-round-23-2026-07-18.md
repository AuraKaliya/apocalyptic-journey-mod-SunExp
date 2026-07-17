# SolarMemory 地图投影边界拆分（第二十三轮开发记录）

> 日期：2026-07-18
>
> 前置提交：`b066b646`（拆分 SolarMemory 固定节点与地图同步边界）
>
> 范围：地图视觉 Hook Adapter、固定槽 Unity 投影、地图卡纹理和层标题

## 1. 本轮结论

本轮完成 SolarMemory 主 Runtime 的第二轮职责迁移。地图固定槽的 Unity 对象创建、复用、纹理应用、链条补齐和层标题设置已从 `SolarMemoryModeRuntime` 移入独立的 `SolarMemoryMapProjectionRuntime`。

当前形成三段边界：

1. `SolarMemoryMapVisualRuntime`：只注册三个地图表现 Hook；
2. `SolarMemoryMapProjectionRuntime`：执行 MapSelectUI、MapItem、Transform、Texture 和 ObjectGroup 变更；
3. `SolarMemoryModeRuntime`：在 ShowMap 时协调 currentNode 恢复、投影调用和隐藏 Boss 延续。

## 2. Hook 时序保持

三个既有 Hook 和先后关系保持不变：

- `MapSelectUI.DataUpdate` After：设置当前 SolarMemory 层标题；
- `NormalMapManager.MapItemInit` After：宿主创建地图项后覆盖固定槽并按需 `SendNode`；
- `MapSelectUI.ShowMap` After：先确认或恢复 currentNode，再重新投影固定槽并继续待处理的圣者乌娜流程。

没有在 `MapItemInit` Before 阶段重写 MapTree，也没有新增地图层或改变第三层结算路由。

## 3. 地图投影职责

`SolarMemoryMapProjectionRuntime` 负责：

- 从 `SolarMemoryFixedNodeCatalog` 取得当前层固定槽；
- 从 `SunExpConfigIndex` 读取并复制 Map 行；
- 保证投影节点具有 `NodeDice`；
- 创建或复用对应的 MapItem；
- 通过 `FixedSlotVisualState` 避免相同 Map/Node 重复初始化；
- 补齐固定槽 Chain；
- 关闭 `ObjectGroup.blocksRaycasts`；
- 从 `VisualRegistry` 解析日耀事件地图卡；
- 通过 `SunExpResourceCache` 加载自定义纹理和宿主回退模板；
- 通过 `MapItemApi.ApplyCardBackgroundTexture` 处理地图卡渲染器兼容。

没有新增资源路径、视觉注册项、VisualBundle 内容或同步加载缓存。

## 4. 主 Runtime 收缩

`SolarMemoryModeRuntime.cs` 从上一轮的 1652 行降至 1266 行，并移除对 `UnityEngine`、`UnityEngine.UI`、Texture、GameObject、Transform 和固定槽 MonoBehaviour 的直接依赖。

主 Runtime 仍保留：

- 模式 Hook 和模式入口初始化；
- 地图生成/选择协调；
- currentNode、保存节点和 NodeDice 恢复；
- 战斗逃跑、战败、Boss 对话与最终结算；
- 牌组与事件卡清理。

## 5. 架构护栏

更新后的门禁要求：

- `SolarMemoryMapVisualRuntime` 必须拥有三个地图视觉 Hook 注册；
- DataUpdate 和 MapItemInit 必须委派给投影 Runtime；
- ShowMap 必须继续进入模式协调器；
- 视觉注册表、资源缓存、MapItemApi 和 raycast 安全必须位于投影 Runtime；
- 主 Runtime 禁止重新引入 `using UnityEngine` 或 `FixedSlotVisualState`；
- 主 Runtime 的固定槽重投影必须委派给投影 Runtime。

## 6. 兼容性

本轮没有修改：

- 固定节点布局和 Map/Event/Level id；
- `SendNode` 条件；
- MapItem.Init 的节点输入；
- VisualRegistry id `solar_memory.event_map_card`；
- 故事牌和建筑牌回退模板；
- currentNode、同步数组或多人权威规则；
- Boss、结局、整备、RPC 和内容数据。

## 7. 完整验证结果

本轮已通过：

- SunExp Release 构建：0 警告、0 错误；
- SunExp 架构断言；
- SunExp C#：312 项行为断言和源码护栏；
- SolarMemory/Event 校验：6 个事件、10 个地图行、0 警告；
- SunExp 全量内容校验：56 张卡牌、13 个遗物、33 个 Buff、5 个卡包、3 个敌人、0 警告；
- 三个主消费者 Release 构建：均为 0 警告、0 错误；
- Aura.Shared：1228 项公共 API 兼容基线；
- Aura.Shared DLL 打包一致性检查。

最终构建产物和三个打包副本的 SHA-256 均为 `A45F9B72230318FC380B020F6BE2A8E30D26F0F681CFF51565D940CE26CFFD50`。

## 8. 后续方向

SolarMemory 地图生成、同步规则和地图投影边界现已稳定。下一轮可进入 `SolarMemoryBattleExitCoordinator`，将逃跑、战败、临时 UI 清理和 transition currentNode 修复从主 Runtime 中拆出；BossTransition 与 Settlement 应继续作为后续独立批次处理。
