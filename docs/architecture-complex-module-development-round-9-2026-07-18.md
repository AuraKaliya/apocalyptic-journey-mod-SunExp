# AuraCg Network Runtime 第九轮开发记录

> 日期：2026-07-18
> 前置工作：第八轮 Overlay Presenter 拆分
> 范围：fight session、sender authority、playId、relay、payload budget 与网络去重

## 1. 开发目标

第八轮完成 Overlay Presenter 后，`SkillCgArbiterComponent` 仍直接持有 fight token、播放 claim 池、本地 action 到 playId 的复用窗口和计数器，并负责玩家/状态所有权校验、服务端请求验证、RPC 发送、payload 预算和远端注册项解析后的入队。

本轮目标是把 Skill CG 的多人 presentation-event 状态机迁入独立 Network Runtime，同时保持：

- 本地 owner 才能发起同步播放；
- 客户端只能发送 server-bound request；
- 服务端从接收上下文绑定 sender，不信任 payload issuer；
- 服务端验证 sender 对 OwnerStatusId 的所有权；
- 网络只传注册身份和 action/session identity，不传媒体路径或表现参数；
- Component 继续负责 Unity 播放队列、Coroutine 和 Presenter。

## 2. AuraCgNetworkRuntime

新增 `AuraCgNetworkRuntime`，统一负责：

- fight session token 创建、接收、清理和过期判断；
- 本地玩家与本地 Status 所有权验证；
- 服务端 sender availability、lobby member 和 owner status 验证；
- 本地播放 snapshot 构造与注册 CG ID 投影；
- client request、host relay 和 authorized playback broadcast；
- 收到 snapshot 后的协议形状、标识长度和序列化字节预算检查；
- host 校验时忽略 recipient-local activation，接收端解析时应用本地 effective activation；
- 远端注册身份解析完成后的组件入队回调。

Network Runtime 不持有 `AuraCgPlaybackCoordinator`、Overlay 或 Coroutine runner。它通过注册表解析委托获得本地 `SkillCgRequest`，通过入队委托把验证后的请求交还组件。

协议预算保持不变：

- 每次播放最多 4 个事件；
- payload soft limit 为 8192 字节；
- 网络标识最大长度为 160；
- playback claim 池最大为 512。

## 3. AuraCgNetworkSessionState

新增无 Unity/Witch 依赖的 `AuraCgNetworkSessionState`，集中拥有：

- 当前 fight token；
- 有界 playback claim store；
- 本地 action 到 playId 的短窗口复用表；
- 单调 local playback counter；
- fight 清理入口。

playId 继续由 issuer、owner status、card、单调计数和 fight token 构成。action key 继续使用 owner、card、action sequence 和 event token。复用窗口仍限制在 0.35–2 秒之间。

fight 清理会清空 token、复用表和 claim 池，但不重置单调计数器，保持原有跨 fight 本地计数行为。

## 4. Runtime 收敛

`AuraCgRuntime.cs` 从第八轮约 2105 行降至约 1597 行。`SkillCgArbiterComponent` 不再声明或直接操作：

- PlayerManager、FightPlayer 或 GameServer 网络状态；
- fight token、playback claim、local playId 表和计数器；
- `SendRpcCommand`；
- server playback validation；
- sender/status ownership；
- payload 字节预算和网络 snapshot normalize；
- fight session RPC 与 playback request/relay。

组件现在只保留三类网络委派：准备本地同步 batch、应用 server/network envelope、把通过验证的本地请求加入播放队列。

## 5. 测试与架构门禁

`AuraCgShared.Tests` 从 131 项增加到 140 项，新增覆盖：

- fight token 规范化；
- playId 复用窗口上下限；
- 同一 action 在窗口内复用 playId；
- token part 清洗和 fight identity 保留；
- 窗口过期后生成新 playId并清理旧 action；
- 首次 claim 与重复 claim；
- fight reset 清理 token、action 表和 claim。

共享架构与 RPC authority 门禁新增要求：

- 网络预算、server validation、sender ownership 和 server-bound request 必须留在 Network Runtime；
- Network Runtime 不得持有播放队列、Overlay 或 Coroutine；
- transient identity 必须留在纯 Session State；
- Component 不得重新访问 PlayerManager/FightPlayer/GameServer、发送 RPC 或持有网络状态；
- host 和 recipient 必须使用不同 activation 解析语义；
- 旧的网络预算源码护栏已更新为新的 Network Runtime 常量。

## 6. 兼容性

本轮未修改：

- `AuraCg.Shared.SkillCgArbiterRuntime+SkillCgArbiterComponent` 完整类型名；
- `CurrentBuildId` 与 `CurrentProtocolVersion`；
- 公共 RPC 类型、公共方法、属性、事件、字段和序列化结构；
- sender hook 和 `IAuraCgServerBoundRpcCommand`；
- fight token、playId、snapshot 和 event 字段；
- payload 限制、claim 容量和 duplicate window；
- 媒体、Overlay 和 Coroutine 播放行为。

## 7. 验证结果

- AuraCg 140 项纯行为断言通过；
- AuraSharedCore 92 项断言通过；
- 1228 项公共 API 兼容基线通过；
- AuraDirector 20 项断言通过；
- AuraToolsExp 632 项断言通过；
- 网络 RPC authority、共享架构和内容/工具/共享边界门禁通过；
- Terrias、SanGuoShaExp、AuraToolsExp 三个主消费者 0 警告、0 错误构建；
- Terrias 架构检查和 282 项 C# 断言通过；
- 共享发布矩阵、消费者打包与三个 `Aura.Shared.dll` 哈希一致性检查通过。

## 8. 下一轮建议

AuraCg 的播放、媒体、Overlay 和网络边界已经关闭。剩余主要职责是 Provider/Registry 编排：

1. 提取 Provider Coordinator，集中 provider 注册、排序、反射调用和请求收集；
2. 提取 Registered Request Resolver，集中 registry identity、activation、资源路径和 request 构造；
3. 让 `SkillCgArbiterComponent` 最终只保留初始化、Unity Update、预加载启动和播放 Coroutine 编排；
4. AuraCg 收尾后进入 AudioArbiter，再处理 BattleBgmArbiter。
