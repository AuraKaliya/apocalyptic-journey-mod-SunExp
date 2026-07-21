# AuraCg Overlay Presenter 第八轮开发记录

> 日期：2026-07-18
> 前置工作：第七轮 Unity Media Repository 拆分
> 范围：Overlay 对象、布局、材质、Flash、Fade 与安全销毁

## 1. 开发目标

第七轮关闭媒体加载边界后，`SkillCgArbiterComponent` 仍直接持有 Overlay 的 Canvas、CanvasGroup、三层 Image、运行时材质和屏幕闪烁 Sprite，并实现 Slide、Fade、Sequence Flash、布局写回与安全销毁。

本轮目标是把 Unity 表现状态和变更集中到单一 Presenter，同时保持：

- `SkillCgArbiterComponent` 继续作为唯一 Coroutine 启动者；
- 播放 generation 继续由 `AuraCgPlaybackCoordinator` 判定；
- Overlay 保持独立 `ScreenSpaceOverlay` Canvas；
- 所有 Graphic 继续禁用 raycast，CanvasGroup 不阻挡输入；
- 公共 API、全局组件身份、BuildId、ProtocolVersion 和 RPC 语义不变。

## 2. AuraCgOverlayPresenter

新增 `AuraCgOverlayPresenter`，统一拥有：

- Overlay Root、Canvas、CanvasGroup、主 Image、Masked Flash Image 和 Screen Flash Image；
- LumaKey、MaskedInvert、ScreenBwFlash 三类运行时材质；
- Slide、Fullscreen Fade、Center Fade 和 Sequence 帧播放；
- Stretch、Contain、Cover 和焦点偏移的 Unity 布局写回；
- 普通定时 Flash、Masked Invert、Screen BW Pulse 与 Hybrid Flash；
- 显示、隐藏、材质销毁、屏幕闪烁 Sprite 销毁和安全 Overlay 销毁。

Presenter 是普通内部类，不继承 `MonoBehaviour`，也不调用 `StartCoroutine`。它只返回 `IEnumerator`；组件负责 `yield return` 并注入 `playbackCoordinator.IsCurrent(generation)`，因此暂停、清理和旧 Coroutine 失效的时序没有改变。

媒体与表现仍保持分离：Presenter 需要 CPU 反色 Sprite 时通过委托调用 `AuraCgUnityMediaRepository.CreateInvertedSprite`，不直接持有媒体缓存。

## 3. 纯表现策略

新增无 Unity/Witch 依赖的 `AuraCgPresentationPolicy`，负责：

- Masked Flash 是否启用；
- Screen BW Flash 是否启用；
- Hybrid 模式同时启用两层；
- 兼容旧的起止帧字段隐式启用 Masked Flash。

布局与轨迹继续由 `AuraCgPresentationMath` 负责，Presenter 只把纯计算结果写入 Unity UI。

## 4. Runtime 收敛

`AuraCgRuntime.cs` 从第七轮约 2929 行降至约 2105 行。`SkillCgArbiterComponent` 不再声明或直接操作：

- Overlay GameObject、Canvas、CanvasGroup 或 Image；
- Shader 查找、Material 创建与材质参数；
- Slide/Fade/Sequence Frames 动画实现；
- Flash 图层状态与布局；
- Overlay 安全隐藏和销毁细节。

组件现在只负责媒体加载、请求日志、generation 检查以及对 Presenter 的 `Show`、`Play`、`Hide` 委派。组件销毁时会显式调用 Presenter 清理其 Unity 资源。

## 5. 测试与架构门禁

`AuraCgShared.Tests` 从 124 项增加到 131 项，新增覆盖：

- MaskedInvert 只启用 Masked 层；
- ScreenBwPulse 只启用 Screen 层；
- HybridBwPulse 同时启用两层；
- 旧帧范围仍隐式启用 Masked 层；
- 普通 Screen Flash 不误启用两个特殊图层。

共享架构门禁新增要求：

- Overlay 状态和 Unity 变更必须留在 `AuraCgOverlayPresenter`；
- Presenter 不得继承 `MonoBehaviour` 或自行启动 Coroutine；
- Component 必须委派 Image/Sequence 显示和动画；
- Component 不得重新声明 Overlay Unity 对象、Shader 查找或动画方法；
- Presenter 必须使用共享 UI 安全销毁路径；
- Flash 选择策略必须保持无 Unity/Witch 依赖。

## 6. 兼容性

本轮未修改：

- `AuraCg.Shared.SkillCgArbiterRuntime+SkillCgArbiterComponent` 完整类型名；
- `CurrentBuildId` 与 `CurrentProtocolVersion`；
- 公共方法、属性、事件、字段和序列化类型；
- RPC sender authority、fight session、playback relay 和跨 MOD 去重；
- 媒体缓存预算、预加载调度与 AssetBundle 释放策略；
- Slide/Fade 时长、布局算法、Flash 帧范围和材质 fallback 顺序。

## 7. 验证结果

- AuraCg 131 项纯行为断言通过；
- AuraSharedCore 92 项断言通过；
- 1228 项公共 API 兼容基线通过；
- AuraDirector 20 项断言通过；
- AuraToolsExp 632 项断言通过；
- 网络 RPC authority、共享架构和内容/工具/共享边界门禁通过；
- Terrias、SanGuoShaExp、AuraToolsExp 三个主消费者 0 警告、0 错误构建；
- Terrias 架构检查和 282 项 C# 断言通过；
- 共享发布矩阵、消费者打包与三个 `Aura.Shared.dll` 哈希一致性检查通过。

## 8. 下一轮建议

AuraCg 的播放协调、媒体适配和 Overlay 表现边界已经完成。剩余最大职责是 Network Runtime 和 Provider/Registry 编排：

1. 提取 Network Runtime，集中 fight session、sender authority 应用、playback request/relay 和 payload validation；
2. 保持 `SkillCgArbiterComponent` 作为 Unity Coroutine 与生命周期入口；
3. 评估 Provider Handle、Registry 查询与请求构造是否需要独立编排服务；
4. AuraCg 收尾后进入 AudioArbiter，再处理 BattleBgmArbiter。
