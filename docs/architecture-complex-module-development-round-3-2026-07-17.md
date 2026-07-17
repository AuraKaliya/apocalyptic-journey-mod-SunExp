# AuraCg Preload 与媒体缓存第三轮开发记录

> 日期：2026-07-17  
> 前置提交：`acc0802 refactor(shared): modularize AuraCg contracts and policies`  
> 范围：Preload 状态协调、媒体缓存所有权和重复强引用清理

## 1. 开发目标

第二轮完成 Registry Query、Network Policy 和 Playback Claim 后，`SkillCgArbiterComponent` 仍直接持有五组 Preload/媒体集合：

- Sprite cache；
- Sequence cache；
- AssetBundle 正向与负向 cache；
- 正在加载的 preload key；
- Adventure preload 去重 key。

本轮将这些状态迁移到拥有明确职责和测试面的对象，同时保持 UnityWebRequest、AssetBundle 异步加载和 Coroutine 的执行位置不变。

## 2. AuraCgPreloadCoordinator

`AuraCgPreloadCoordinator` 现在拥有两类状态：

1. `pendingKeys`：同一媒体只允许一个正在运行的预加载任务。
2. `adventureKeys`：同一 Adventure key 只触发一次批量预加载。

主要改进：

- 预加载协程通过 `try/finally` 调用 `CompletePreload`，加载失败、提前结束或协程释放时不会永久遗留 pending claim；
- Adventure key 使用 HashSet 加 FIFO 顺序队列；
- 历史上限为 128，避免 AuraTools 每次 Adventure 生成新 key 后集合持续增长；
- 淘汰 Adventure key 不会重复加载已缓存媒体，因为实际媒体缓存仍在任务开始前检查。

## 3. AuraCgMediaCache

新增泛型 `AuraCgMediaCache<TSprite, TBundle>`，统一拥有：

- 单帧 Sprite；
- Sequence 帧列表；
- AssetBundle 和加载失败的 null sentinel；
- CPU masked-invert 生成的派生 Sprite。

泛型缓存本身不依赖 Unity 或 Witch。`SkillCgArbiterComponent` 使用 `AuraCgMediaCache<Sprite, AssetBundle>`，但资源创建和异步加载仍留在 Unity Adapter。

`AuraCgMediaCacheKeys` 统一生成 Sprite、Sequence 和 Preload key，保留原来的路径、alpha mode、threshold 和 softness 组合语义。

## 4. 重复强引用修复

旧 Sequence 预加载存在两个 Dictionary key：

- 播放读取的规范 `SequenceCacheKey`；
- 仅预加载流程写入的 `sequence:` 前缀 key。

两者指向同一个 `List<Sprite>`。第二项不会参与正常读取，却会额外保留整组 Sprite 强引用。本轮删除该二级写入，只保留由 `AuraCgMediaCacheKeys.Sequence` 生成的规范键。

这解决了已评审的“二级强引用缓存绕过统一缓存所有权”问题，但本轮没有引入对象数量或估算字节淘汰策略。媒体缓存的内存体积预算仍属于后续性能专项，不能用简单条目数替代。

## 5. 测试与门禁

`AuraCgShared.Tests` 从 37 项增加到 54 项，新增覆盖：

- pending preload 去重与完成释放；
- cached media 跳过；
- Adventure key 去重、FIFO 淘汰与 128 项容量模型；
- Sprite/Sequence/Bundle/派生 Sprite 的统一所有权；
- AssetBundle null sentinel；
- 规范 Sequence/Preload key 和 alpha alias 归一化。

架构门禁禁止 `SkillCgArbiterComponent` 重新声明 Sprite、Sequence、AssetBundle 或 preload key 私有集合，并要求 Preload、Media Cache 和 Cache Keys 保持无 Unity/Witch 依赖。

## 6. 兼容与剩余风险

本轮未修改公共 API、嵌套组件完整名称、RPC、BuildId 或 ProtocolVersion。1228 项程序集兼容基线保持不变。

仍需后续处理：

- 媒体缓存按估算内存体积限制，而不是只按对象数量；
- Sprite、Texture、AssetBundle 和派生资源的安全淘汰/销毁策略；
- Overlay Playback 与媒体加载 Adapter 的进一步文件拆分；
- 预加载队列的全局并发和每帧启动预算。
