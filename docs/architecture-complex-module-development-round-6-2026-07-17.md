# AuraCg 播放协调与表现算法第六轮开发记录

> 日期：2026-07-17
> 前置提交：`737c42a8 perf(shared): bound AuraCg preload scheduling`
> 范围：播放队列状态所有权、Coroutine generation 协调和纯表现算法

## 1. 开发目标

第五轮完成预加载背压后，`SkillCgArbiterComponent` 仍直接持有播放队列、重复窗口、活动标记、入队序号和 Coroutine generation。布局、Cover 焦点、滑入透明度和黑白闪烁脉冲等纯算法也仍位于 Unity Component 内。

本轮目标是：

- 将播放队列及其完整生命周期迁入单一纯协调器；
- 将播放 generation 作为协调器状态，统一清理和过期 Coroutine 判定；
- 将不依赖 Unity 对象的表现计算迁入纯算法服务；
- 保持 Overlay、媒体加载、Coroutine 执行和 Unity 对象变更位置不变；
- 保持公共 API、嵌套组件完整类型名、BuildId、ProtocolVersion 和 RPC 语义不变。

## 2. AuraCgPlaybackCoordinator

新增 `AuraCgPlaybackCoordinator`，统一拥有：

- 有界播放队列；
- `DuplicateKey` 时间窗口；
- 稳定入队序号；
- 当前播放 generation；
- 活动播放循环标记；
- 过期请求过滤和队列完成状态。

`SkillCgArbiterComponent` 不再声明 `queue`、`recentKeys`、`enqueueSequence`、`playing` 或 `playGeneration`。组件显式注入 `Time.unscaledTime`、最大队列长度、重复窗口和请求时效，并继续使用 `StartCoroutine` 执行实际播放。

清理时由协调器一次性递增 generation、清空队列与重复窗口并释放活动标记。旧 Coroutine 通过 `IsCurrent` 失效，不能在清理后重新提交表现状态。

播放开始改为先取得唯一 active claim，再启动 Coroutine。重复启动请求不会创建并行播放循环；若 Coroutine 启动同步抛出异常，active claim 会在重新抛出前释放。

## 3. AuraCgPresentationMath

新增无 Unity/Witch 依赖的 `AuraCgPresentationMath`，负责：

- Slide 图片尺寸；
- Cover 图片尺寸和 safe scale；
- Cover 焦点到溢出偏移的映射；
- Slide X 轨迹和透明度；
- Sequence 黑白闪烁脉冲衰减。

Unity Runtime 只负责把 `Sprite.rect` 与 viewport 转换成标量输入，再把结果写回 `Vector2` 和 UI 组件。本轮没有改变 Canvas、材质、射线、动画时长或 Coroutine 帧序。

原位于 `AuraCgPresentationContracts.cs` 的内部 `QueuedRequest` 已迁入播放协调器，避免 Presentation Contracts 继续承载运行时队列实现。

## 4. 测试与架构门禁

`AuraCgShared.Tests` 从 97 项增加到 118 项，新增覆盖：

- 空请求与空媒体拒绝；
- 队列容量、稳定顺序和相同优先级下的旧项淘汰；
- 重复窗口拒绝与到期重试；
- 单一活动 generation；
- 过期请求跳过；
- 清理后旧 generation 失效；
- Slide/Center/End 轨迹锚点；
- Slide 边缘透明度；
- 横向与纵向 Cover 尺寸；
- Cover 焦点偏移；
- 黑白闪烁脉冲表。

共享架构门禁新增要求：

- Playback Coordinator 与 Presentation Math 必须保持无 Unity/Witch 依赖；
- `SkillCgArbiterComponent` 不得重新声明播放队列、重复窗口、generation 或活动循环状态；
- 预加载暂停和媒体安全释放必须读取协调器的活动状态。

`Test-ContentToolSharedBoundary.ps1` 同时修复了既有的 PowerShell `-Include` 误报：源码扫描现在显式限制为 `.cs` 与 `.csproj`，不会把共享目录 README 中用于解释所有权的示例误判为运行时依赖泄漏。

## 5. 兼容性与验证

本轮未修改：

- `AuraCg.Shared.SkillCgArbiterRuntime+SkillCgArbiterComponent` 完整类型名；
- `CurrentBuildId`；
- `CurrentProtocolVersion`；
- 公开方法、属性、事件、字段和序列化类型；
- RPC command 与 sender authority；
- Overlay、媒体缓存、预加载和资源释放策略。

验证结果：

- AuraCg 118 项纯行为断言通过；
- AuraSharedCore 92 项断言通过；
- 1228 项程序集公共 API 兼容基线通过；
- AuraDirector 20 项断言通过；
- AuraToolsExp 632 项断言通过；
- 网络 RPC authority、共享架构与内容/工具/共享边界门禁通过；
- Terrias、SanGuoShaExp、AuraToolsExp 三个主消费者 0 警告、0 错误构建；
- Terrias 架构测试和 282 项 C# 断言通过；
- 共享发布矩阵和 `Aura.Shared.dll` 打包一致性门禁通过。

## 6. 剩余工作

`AuraCgRuntime.cs` 当前约 3360 行。播放状态所有权已经移出，但 Unity 表现和媒体加载仍是主要剩余职责：

1. 提取 Unity Media Repository，统一 Sprite、Sequence、Bundle 加载与 Cache/Release Queue 接入；
2. 提取 Overlay Presenter，集中 Canvas、Image、材质、Flash、Fade 和安全清理；
3. 提取 Network Runtime，收口 fight session、sender authority 应用和播放 relay；
4. 在这些边界稳定后，再评估 Provider Registry 是否需要独立服务。

下一轮建议优先处理 Unity Media Repository。它已经有纯缓存、预加载调度和释放队列作为稳定下游边界，拆分风险低于直接迁移 Overlay Coroutine。
