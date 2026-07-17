# AuraCg 模块治理第二轮开发记录

> 日期：2026-07-17  
> 前置开发：`architecture-complex-module-development-round-1-2026-07-17.md`  
> 范围：AuraCg 无行为源码拆分，以及 Registry Query、Network Policy、Playback Claim 三个纯逻辑边界

## 1. 开发约束

本轮遵守以下兼容条件：

- `SkillCgArbiterRuntime` 公共 Facade 保持不变；
- `AuraCg.Shared.SkillCgArbiterRuntime+SkillCgArbiterComponent` 完整类型名保持不变；
- `CurrentBuildId`、`CurrentProtocolVersion` 和 RPC 字段不变；
- Unity Overlay、Coroutine、Sprite、AssetBundle 和材质生命周期仍由运行时组件持有；
- sender authority、fight token、payload budget 和远端本地激活策略不变。

## 2. 源码组织结果

原 `AuraCgRuntime.cs` 约 4250 行。本轮将公开和网络契约机械迁移到独立文件，并把主文件收缩到约 3250 行：

| 文件 | 职责 |
| --- | --- |
| `AuraCgContracts.cs` | Arbiter Options、Registry View、Trigger Context、Request 及其规范化 |
| `AuraCgNetworkContracts.cs` | Network Event、Playback Snapshot 和内部网络 Envelope |
| `AuraCgRpc.cs` | RPC sender、authority binding 与三个 RPC Command |
| `AuraCgPresentationContracts.cs` | Media、Alpha、Flash、Presentation 和 Fit 常量/规范化 |
| `AuraCgRuntime.cs` | 公共 Facade、全局组件兼容、Unity 生命周期和播放协调 |

文件迁移没有改变命名空间、类型可见性或公开成员。兼容基线同时开始记录公开类型的 `Serializable` 标记，防止以后拆文件时遗漏序列化身份。

## 3. 提取的纯服务

### 3.1 AuraCgRegistryQueryService

负责：

- Registry 条目的 enabled、kind 和 media 类型判断；
- role、card、action 与 consumer activation 匹配；
- 主资源和 fallback 资源选择；
- 从 Registry Entry 与 Trigger Context 构造播放请求。

服务不读取 Unity 时间。Runtime 显式传入 `createdAt`，因此纯测试可以稳定验证请求映射。

### 3.2 AuraCgNetworkPolicy

负责：

- 网络标识长度限制；
- 单个 Network Event 的注册身份形状；
- Playback Snapshot 的事件数量与必需标识形状；
- Snapshot 和子事件的 authority 字段归一化；
- 播放去重键生成。

序列化字节预算仍由 `AuraSharedPayloadBudget` 执行，游戏状态授权仍留在 Runtime Adapter，纯策略不依赖 Mirror、Witch 或 Unity。

### 3.3 AuraCgPlaybackClaimStore

原组件内的 HashSet 和 Queue 已合并为一个有明确 owner 的生命周期对象：

- 容量继续使用 `MaxPlaybackPoolEntries`；
- 重复 issuer/playId 继续拒绝；
- 超出容量时继续淘汰最旧 claim；
- 战斗清理通过单一 `Clear()` 同时清空索引和顺序队列。

## 4. 测试与门禁

新增 `AuraCgShared.Tests`，当前包含 37 项纯行为断言，覆盖：

- Registry kind/media/role/card/activation 匹配；
- 原有前导 `*` 卡牌 ID 兼容语义；
- 请求字段、展示参数和时钟注入；
- 网络标识、事件数量和 Snapshot 归一化；
- 去重、容量淘汰和战斗清理。

共享发布矩阵新增 `auracg-behavior` 步骤。架构门禁要求三个纯服务持续独立于 Unity、Witch、PlayerManager、GameObject 和 MonoBehaviour。

## 5. 本轮结论

本轮完成的是 AuraCg 的第一组稳定服务边界，而不是把所有 Unity 播放实现继续细分。当前 Facade 已把 Registry 匹配、网络形状验证和播放 claim 所有权委托出去；Overlay、Preload、Sprite/Sequence 和 Flash 仍集中在 Unity Component 内，避免在同一批次迁移协程和资源生命周期。

后续 AuraCg 若继续收缩，应优先处理 Preload Coordinator 和媒体加载缓存所有权，再考虑拆分 Overlay Playback。下一项跨模块开发则可以进入 Audio Arbiter 的 Contracts、Provider Resolver 和 Presentation Policy。
