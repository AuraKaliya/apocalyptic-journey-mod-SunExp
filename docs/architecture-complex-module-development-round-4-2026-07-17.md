# AuraCg 媒体缓存预算与安全淘汰第四轮开发记录

> 日期：2026-07-17  
> 前置提交：`1e860e0 refactor(shared): centralize AuraCg preload and media caches`  
> 范围：估算字节预算、全局 LRU、资源实例引用账本和 Unity 安全释放

## 1. 开发目标

第三轮统一了媒体缓存所有权，但缓存仍然只增长、不按内存体积治理。单帧图片数量相同并不代表成本相同，Sequence 还可能通过多个键重复引用同一 Sprite，因此简单的条目上限既不能反映内存风险，也可能在淘汰时提前销毁仍被其他缓存项使用的资源。

本轮目标是：

- 使用估算字节和条目数双重上限；
- Sprite、Sequence、AssetBundle 和派生 Sprite 共用一个 LRU 顺序；
- 同一资源实例跨缓存项只计量一次；
- 只在最后一个缓存引用释放后生成资源释放通知；
- Unity 对象销毁延迟到没有播放和预加载任务的空闲点。

## 2. 模块拆分

缓存性能逻辑没有重新堆回 `SkillCgArbiterComponent`，也没有形成单个超大缓存文件：

- `AuraCgMediaCache`：缓存门面、四类索引、全局 LRU 和容量执行；
- `AuraCgMediaRetentionLedger`：按对象引用身份维护引用计数、估算字节和最终释放通知；
- `AuraCgMediaCacheModels`：缓存项、所有权类型、统计快照和引用比较器；
- `AuraCgMediaReleaseQueue`：合并释放通知，并在播放/预加载结束后的安全点执行保留状态复查；
- `SkillCgArbiterComponent`：只负责 Unity 估算接入和安全时机下的 Destroy/AssetBundle.Unload。

三个缓存模块都不依赖 Unity 或 Witch，Unity Adapter 仍然保留异步加载与对象生命周期操作。

## 3. 容量与 LRU 策略

当前运行时采用两个硬边界：

- 最多 512 个缓存项；
- 最多保留约 256 MiB 的估算媒体体积。

缓存命中会刷新全局 recency。写入后只要任一边界超限，就持续淘汰最久未使用的项，直到重新进入预算。单个资源自身超过预算时仍可供当前调用方使用，但不会继续留在缓存中。

AssetBundle 的 null 负缓存仍参与条目上限，但估算字节为零，避免大量不存在路径无限累积。

## 4. 共享实例账本

文件 Sequence 的帧既可能由单帧键引用，也会由 Sequence 项引用。账本使用对象引用身份而不是 Unity 相等语义建立资源记录：

- 同一个 Sprite 无论被多少缓存项引用，只计量一次；
- 淘汰某一个键只减少引用计数；
- 最后一个缓存引用消失时才发出释放通知；
- 替换同一个键时先挂接新资源再移除旧项，避免共享实例产生短暂的零引用和错误释放。

这使字节预算可以真正覆盖 Sequence 的二级强引用，而不是只治理最外层 Dictionary。

## 5. Unity 安全释放

缓存层只报告“资源已不再被缓存保留”，不直接操作 Unity：

- 文件加载 Sprite 和 CPU 派生 Sprite 标记为运行时拥有，空闲释放时同时销毁 Sprite 与其 Texture；
- 本地加载的 AssetBundle 淘汰后调用 `Unload(false)`，不强制破坏已经加载并可能仍在使用的资产；
- Bundle 内直接加载的 Sprite 作为外部资产处理，缓存只释放强引用，不擅自 Destroy；
- 当 CG 正在播放或仍有任一 preload claim 时，释放通知进入延迟队列；
- 到达空闲点后再次检查资源是否被缓存重新保留，重新保留的资源不会被旧通知销毁。

这里没有在每次淘汰时调用 `Resources.UnloadUnusedAssets`，因为它可能制造明显的主线程停顿。Bundle 外部资产交由 Unity 正常的未使用资源回收时机处理。

## 6. 测试与门禁

`AuraCgShared.Tests` 当前包含 79 项断言，新增覆盖：

- LRU 命中刷新与最旧项淘汰；
- 估算字节和条目双重边界；
- 同一 Sprite 跨单帧/Sequence 只计量一次；
- Sequence 替换保留共享实例且只释放被移除的帧；
- 最后引用释放且只通知一次；
- Clear 后账本归零；
- 超大资源不留存；
- AssetBundle 负缓存与句柄释放。
- 活跃加载期间延迟释放、通知合并、重新保留复查和后续再次淘汰。

架构门禁要求字节预算、全局 LRU、引用计数、引用身份比较和延迟 Unity 释放继续存在，并要求缓存四个模块保持无 Unity/Witch 依赖。公共程序集兼容基线仍为 1228 项。

完整共享发布门禁已通过，包括 AuraSharedCore 92 项、AuraCg 79 项、AuraDirector 20 项、AuraToolsExp 632 项、三个主消费者构建及打包 DLL 哈希一致性。SunExp C# 构建为 0 警告、0 错误，282 项源码断言通过。

## 7. 剩余风险

估算体积是治理指标，不是精确显存：Sprite 按纹理像素近似，AssetBundle 句柄使用固定估算，驱动和压缩格式开销不会完全反映。

后续仍需处理：

- 预加载队列的全局待处理上限、并发数和每帧启动预算；
- Overlay Playback 与媒体加载 Unity Adapter 的进一步文件拆分；
- 如真实运行观测表明 256 MiB 默认值不合适，再引入不破坏跨 MOD 配置所有权的可调策略。
