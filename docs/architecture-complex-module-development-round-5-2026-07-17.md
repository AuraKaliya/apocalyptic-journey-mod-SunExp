# AuraCg 预加载背压与帧预算第五轮开发记录

> 日期：2026-07-17  
> 前置提交：`c6dcd5f perf(shared): bound AuraCg media cache memory`  
> 范围：预加载待处理上限、owner 公平、并发上限和每帧启动预算

## 1. 开发目标

第四轮限制了缓存保留量，但 `PreloadCg` 仍会为批次内每个未缓存请求立即调用 `StartCoroutine`。消费侧虽然依赖 Unity 异步加载自然让出帧，生产侧却没有总量、owner 或并发背压。大注册表或多个 MOD 同时预热时，可能在同一帧创建大量协程和 UnityWebRequest。

本轮将预加载改为先进入纯调度器，再由 Unity Adapter 按帧领取任务。预加载仍然是非关键优化；超出容量的任务可以丢弃，实际展示时继续走原有按需加载路径，不影响 CG 正确性。

## 2. 模块边界

原 `AuraCgPreloadCoordinator` 同时承担 pending claim 和 Adventure 历史。引入队列后，这两个生命周期不再适合继续混在同一类型中，因此拆分为：

- `AuraCgPreloadScheduler<TRequest>`：入队准入、跨 owner 去重、容量、owner 轮转、并发 claim 和完成释放；
- `AuraCgPreloadSubmission<T>`：只枚举到单次上限外一项，报告截断且不完整物化生产者序列；
- `AuraCgAdventurePreloadHistory`：只维护有界 Adventure 去重历史；
- `SkillCgArbiterComponent.Update`：每帧从纯调度器领取少量任务并启动 Unity Coroutine；
- `PreloadRequest`：保持具体 Sprite、Sequence、AssetBundle 加载，并在 `finally` 完成 active claim。

两个调度模块均不依赖 Unity 或 Witch。UnityWebRequest、Coroutine 和 AssetBundle 操作没有下沉到策略层。

## 3. 准入与背压

当前运行时边界为：

- 单次 API 提交最多检查 256 项，避免在进入有界队列前完整物化超大枚举；
- 全局 pending 上限 128，包含排队和正在运行的任务；
- 单 owner pending 上限 64；
- 全局同时运行上限 2；
- 每帧最多启动 1 个新预加载协程。

同一规范媒体 key 在所有 owner 之间只保留一份 claim。准入结果区分已缓存、重复、容量超限和无效请求。容量超限会累计计数，并按 owner 输出一次警告；任务本身不进入任何隐藏的二级队列。

单 owner 上限按调用 `PreloadCg` 的生产者计费，而不是按被引用资源的内容 owner 计费。这样 AuraTools 预热其他内容 MOD 的资源时不会消耗对方额度。该上限保证一个内容 MOD 或工具 MOD 不能占满全部 128 个位置，给其他消费者保留至少一半全局容量。

## 4. Owner 公平与播放优先

调度器为每个 owner 保存独立 FIFO，并使用 owner rotation 轮转领取。只要多个 owner 都有排队任务，连续启动会在 owner 之间交替，而不是按大批次的原始入队顺序让一个生产者长期独占。

`SkillCgArbiterComponent.Update` 在 CG 正在播放时不会启动新 preload。已经运行的最多两个异步任务不会被强制取消，因为中断 UnityWebRequest 或 AssetBundle 加载可能留下更复杂的清理状态；新任务会等播放结束后再按帧恢复。

任务真正启动前会再次检查媒体缓存。排队期间若按需播放已经加载了同一媒体，该 preload claim 会直接完成，不再重复发起 I/O。

## 5. 完成与资源释放

调度器只允许 active key 进入 `Complete`，未知或重复完成不会破坏计数。`PreloadRequest` 继续在 `finally` 中完成 claim，因此加载失败、协程异常或提前结束都能释放并发位置。

第四轮的媒体释放队列现在只等待 `ActiveCount == 0`，不等待尚未启动的 queued 请求。排队项不持有 Unity 媒体对象，因此不会无意义地延长已淘汰资源的生命周期。

## 6. 测试与门禁

`AuraCgShared.Tests` 从 79 项增加到 97 项，新增覆盖：

- 无效、已缓存和跨 owner 重复请求；
- 全局 pending 与单 owner pending 上限；
- 容量拒绝计数；
- 每次领取的启动预算；
- 全局并发上限；
- owner round-robin 启动顺序；
- 完成释放、未知完成保护和同 key 重试；
- Adventure 历史继续保持有界。
- 超大提交只保留有限前缀，并且最多多探测一个元素。

架构门禁要求提交检查量、pending、owner pending、并发和每帧启动预算都保持有限，要求 owner rotation、容量拒绝观测和 `finally` 完成路径存在，并禁止纯调度模块依赖 Unity/Witch。公共 API、BuildId、ProtocolVersion 和 RPC 语义未改变，程序集兼容基线仍为 1228 项。

完整共享发布门禁已通过，包括 AuraSharedCore 92 项、AuraCg 97 项、AuraDirector 20 项、AuraToolsExp 632 项、三个主消费者构建及打包 DLL 哈希一致性。SunExp C# 构建为 0 警告、0 错误，282 项源码断言通过。

## 7. 剩余工作

当前背压限制的是 AuraCg preload，不修改共享 `AuraSharedFrameScheduler` 的通用 soft-watermark 语义。后续仍可继续：

- 将 Overlay Playback 和媒体加载 Unity Adapter 从 `AuraCgRuntime.cs` 进一步拆分；
- 增加实际运行时 preload 等待时长、拒绝率和加载耗时观测，再决定是否调整 128/64/2/1 默认值；
- 如需在战斗期间暂停已经运行的序列预热，应设计可恢复的 cooperative cancellation，而不是直接停止协程。
