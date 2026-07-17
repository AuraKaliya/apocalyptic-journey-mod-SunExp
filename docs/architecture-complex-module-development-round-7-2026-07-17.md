# AuraCg Unity Media Repository 第七轮开发记录

> 日期：2026-07-17
> 前置工作：第六轮 Playback Coordinator 与 Presentation Math 拆分
> 范围：Sprite、Sequence、AssetBundle 加载，媒体缓存与延迟释放的 Unity Adapter

## 1. 开发目标

第六轮完成播放状态和纯表现算法迁移后，`SkillCgArbiterComponent` 仍直接持有 Unity 媒体缓存、释放队列、文件纹理加载、AssetBundle 加载、序列枚举和 CPU 像素处理。

本轮目标是建立单一 Unity Media Repository，使组件只负责：

- 在 Unity 主线程上启动和串联 Coroutine；
- 提供播放与预加载是否空闲的安全释放边界；
- 把加载结果交给现有播放和 Overlay 流程；
- 保持公共 API、嵌套组件身份、网络协议和资源语义不变。

## 2. 新边界

新增 `AuraCgUnityMediaRepository`，统一负责：

- `AuraCgMediaCache<Sprite, AssetBundle>` 的唯一实例化与预算配置；
- `AuraCgMediaReleaseQueue<Sprite, AssetBundle>` 的接入与安全刷新；
- PNG/JPG 文件纹理的 `UnityWebRequestTexture` 加载；
- 文件序列与 AssetBundle 序列的加载编排；
- AssetBundle 注册项复用、磁盘加载和 `Unload(false)` 释放；
- CPU BlackKey alpha fallback；
- Masked Invert 派生 Sprite 的创建和二级缓存。

Repository 是普通内部类，不继承 `MonoBehaviour`，也不自行启动 Coroutine。其 `IEnumerator` 仍由 `SkillCgArbiterComponent.StartCoroutine` 所在的 Unity 主线程执行。

组件保留 `ShouldApplyCpuAlphaMode` 回调，因为是否使用 CPU fallback 仍取决于 Overlay Presenter 当前能否解析 LumaKey 材质。该回调是本轮有意保留的表现层到媒体适配器依赖点。

## 3. 路径解析

新增无 Unity/Witch 依赖的 `AuraCgMediaPathResolver`，集中负责：

- 资源相对路径与 Bundle ID 规范化；
- 支持帧格式判断；
- Bundle 序列前缀匹配；
- 文件序列过滤与稳定排序。

原有公共路径解析入口继续保持不变，内部委派给该纯解析器。

## 4. Runtime 收敛

`AuraCgRuntime.cs` 从第六轮约 3360 行降至约 2930 行。`SkillCgArbiterComponent` 不再声明或直接操作：

- Unity 类型媒体缓存和释放队列；
- `UnityWebRequestTexture` / `DownloadHandlerTexture`；
- `AssetBundle.LoadFromFile` / `AssetBundle.Unload`；
- Sprite、Sequence、Bundle 的底层加载方法；
- 派生反色 Sprite 的像素创建和缓存。

预加载、单图播放、序列播放继续保留原有调用时序，只把媒体访问改为 Repository 委派。释放时仍由组件计算 `!playbackCoordinator.IsPlaying && preloadScheduler.ActiveCount == 0`，Repository 在真正销毁前继续复查资源是否已被缓存重新持有。

## 5. 测试与架构门禁

`AuraCgShared.Tests` 从 118 项增加到 124 项，新增覆盖：

- 资源路径和 Bundle ID 规范化；
- PNG/JPG/JPEG 支持范围；
- Bundle 序列前缀包含与排除；
- 文件序列过滤和大小写无关的确定性排序。

共享架构门禁新增要求：

- Unity Media Repository 必须维持 512 项和 256 MiB 双预算；
- Unity 文件纹理与 AssetBundle 原语必须留在 Repository；
- `SkillCgArbiterComponent` 不得重新持有媒体缓存、释放队列或底层加载方法；
- 路径解析器必须保持无 Unity/Witch 依赖；
- `Unload(false)`、重新持有复查和播放/预加载空闲边界不得回退。

## 6. 兼容性

本轮未修改：

- `AuraCg.Shared.SkillCgArbiterRuntime+SkillCgArbiterComponent` 完整类型名；
- `CurrentBuildId` 与 `CurrentProtocolVersion`；
- 公共方法、属性、事件、字段和序列化类型；
- RPC sender authority、fight session 和 playback relay 语义；
- Overlay Canvas、材质、动画和清理流程；
- 缓存预算、预加载调度限制与 AssetBundle 释放策略。

## 7. 验证结果

- AuraCg 124 项纯行为断言通过；
- AuraSharedCore 92 项断言通过；
- 1228 项公共 API 兼容基线通过；
- AuraDirector 20 项断言通过；
- AuraToolsExp 632 项断言通过；
- 网络 RPC authority、共享架构和内容/工具/共享边界门禁通过；
- SunExp、SanGuoShaExp、AuraToolsExp 三个主消费者 0 警告、0 错误构建；
- SunExp 架构检查和 282 项 C# 断言通过；
- 共享发布矩阵、消费者打包与三个 `Aura.Shared.dll` 哈希一致性检查通过。

## 8. 下一轮建议

AuraCg 的媒体适配边界已经关闭。剩余最大职责是 Overlay Presenter，其次是网络编排：

1. 提取 Overlay Presenter，集中 Canvas、Image、Material、Flash、Fade、布局写回和安全销毁；
2. Presenter 仍由组件 Coroutine 驱动，不改变动画帧序与输入屏蔽策略；
3. 再评估 Network Runtime，把 fight session、sender authority 应用和 playback relay 从组件移出；
4. AuraCg 完成后进入 AudioArbiter，再处理 BattleBgmArbiter。
