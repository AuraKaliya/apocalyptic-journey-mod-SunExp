# AudioArbiter Network Runtime 与 Session State 第十四轮开发记录

> 日期：2026-07-18  
> 前置工作：第十一至十三轮已拆出 Contracts、Manifest、Match Policy、Provider Resolver、Presentation Policy、Replacement Coordinator 与 Unity Playback Service  
> 范围：Audio 网络策略、fight-scoped 会话状态、RPC 适配、sender authority、TTL、重复抑制、测试与发布护栏

## 1. 本轮目标

本轮关闭 `AudioArbiterComponent` 中剩余的网络与临时会话职责：

1. 提取 card-use presentation 的事件分类、TTL 与服务端 envelope 校验；
2. 提取 fight token、play-id 复用、bounded claim ledger 与生命周期清理；
3. 将 `PlayerManager.SendRpcCommand*`、host/client relay 和 sender ownership 留在专用网络适配层；
4. 保持全局组件、公开入口、RPC DTO 与协议版本不变；
5. 用纯行为测试覆盖过期、重复、跨 fight、错误 sender/owner 和容量淘汰。

## 2. AudioNetworkPolicy

新增 `AudioNetworkPolicy.cs`，集中负责无 Unity、无游戏管理器、无 RPC transport 的纯规则：

- `CardUse` presentation 分类；
- 按调用方传入时钟判断 `CreatedAtUtcTicks + MaxAgeMilliseconds` 是否过期；
- 生成 `fightToken + issuer + eventId` 去重键；
- 校验 bound sender 是否存在、是否在 lobby；
- 校验 event/fight/card/status 必填字段与长度上限；
- 拒绝 payload issuer 与 bound sender 不一致；
- 通过注入的 ownership 函数验证 sender 拥有提交的 status；
- 拒绝非法 max-age 与已经过期的客户端 presentation request；
- 联机时拒绝 issuer 或 owner status 为空的本地 presentation。

服务端过期校验现在发生在更新时间戳之前。此前服务端会先写入新的 `CreatedAtUtcTicks`，使客户端原始 TTL 不能形成真正的拒绝边界；本轮在不改变 payload 字段的前提下关闭了该缺口。

## 3. AudioNetworkSessionState

新增 `AudioNetworkSessionState.cs`，成为 fight-scoped 临时网络身份的唯一所有者：

- 当前 fight token；
- 最多 512 项的 presentation claim 集合与 FIFO 淘汰队列；
- 本地 action key 到 play id 的短窗复用；
- 0.15 秒复用边界与 1 秒旧 action 清理；
- fight 切换时 claim、复用表和 local counter 的统一重置；
- solo 与 multiplayer 不同的 fight-token 验证规则。

该状态对象不读取 `Time`、`DateTime.UtcNow`、`PlayerManager` 或 Unity 对象。所有时钟和多人状态均由 adapter 显式传入，因此容量、边界时间和生命周期可以独立测试。

## 4. AudioNetworkRuntime

新增 `AudioNetworkRuntime.cs`，集中接管必须依赖游戏网络环境的编排：

- fight start 时生成 host token，并广播 `RpcAudioFightSession`；
- client 等待 host fight session，solo 创建本地 token；
- 本地 card-use presentation 补齐 issuer、fight token、时间戳和 TTL；
- host 本地事件广播，client 本地事件提交 `RpcAudioPresentationRequest`；
- 服务端把 `AuraRpcSender` 投影为只读策略输入，校验后才重写 issuer 并 relay；
- `TempDataManager.RoleStatusMap` ownership 查询；
- 普通非 card-use 音频的 `SendRpcCommandExcludeOwner` 同步；
- 远端 presentation 的过期、fight、duplicate claim 入口；
- 本地 action play-id 的生成与复用。

`AudioArbiterComponent` 不再直接调用 `SendRpcCommand*`，也不再持有 fight token、presentation claim、recent play-id 或 local counter。Hook、Coroutine、provider/clip 解析和播放编排仍由原组件负责，避免产生第二个 Unity 生命周期所有者。

## 5. Runtime 收敛结果

本轮 `AudioArbiterRuntime.cs` 从约 2338 行降至 2057 行，减少约 281 行；相对最初约 3276 行累计减少约 1219 行。

新增文件规模：

- `AudioNetworkPolicy.cs`：约 90 行；
- `AudioNetworkSessionState.cs`：126 行；
- `AudioNetworkRuntime.cs`：约 307 行。

Runtime 仍然较大，但剩余主体已经主要是 Hook 接入、请求编排、provider adapter、低血量/语音/战斗结果触发和日志，不再混合网络 session ledger 与 RPC relay 实现。

## 6. 测试与门禁

`AudioArbiterShared.Tests` 从 236 项增至 282 项，新增覆盖：

- TTL 截止点仍有效、超过一个 tick 后过期；
- 非 presentation 与未设置 TTL 的兼容行为；
- 去重键和大小写无关的事件分类；
- multiplayer 本地 issuer/owner 缺失拒绝；
- sender missing、lobby membership、issuer mismatch、owner mismatch；
- event/fight/card/status 字段与长度上限；
- max-age 下限、上限和服务端过期拒绝；
- fight session 未就绪、跨 fight、重复 claim；
- 有界 claim 淘汰后旧事件可重新占用；
- solo claim；
- 0.15 秒 play-id 复用边界、超窗递增、旧 key 清理；
- fight reset 清空 token、claim、复用表和 counter。

架构和 authority 门禁新增要求：

- Policy 与 Session State 不得依赖 Unity、Witch、RPC transport 或全局时钟；
- Network Runtime 不得拥有 AudioClip、AudioSource、Coroutine 或播放队列；
- Component 不得恢复 `SendRpcCommand*`、fight token、claim ledger、recent play-id、counter 或 ownership 校验；
- server validation 必须使用 bound sender，并在更新时间戳前检查客户端 TTL；
- 本地联机 presentation 必须具有明确 issuer 与 owner status；
- 512 项容量、fight reset 和 play-id reuse 必须由 Session State 持有。

## 7. 兼容性

本轮未修改：

- 全局组件完整类型名；
- `CurrentBuildId = audio-arbiter-2026-07-11-v8`；
- `CurrentProtocolVersion = 6` 与 minimum protocol；
- 公开 API 与序列化 DTO 字段；
- RPC command 类型和 sender binding 入口；
- provider identity、bare ProviderId 兼容与 OwnerModId 消歧；
- 0.15 秒复用窗、512 claim 上限和默认 10 秒 presentation TTL。

服务端真正拒绝已过期客户端请求，以及联机本地请求缺少 issuer/owner 时 fail closed，属于既有 authority/TTL 契约的落实，不改变 wire shape，因此无需提升 BuildId 或 ProtocolVersion。

## 8. 完整验证结果

- AudioArbiterShared：282 项断言通过；
- AuraCgShared：153 项断言通过；
- AuraSharedCore：92 项断言通过；
- Aura.Shared：1228 项公共 API 兼容基线通过；
- AuraDirector：20 项断言通过；
- AuraToolsExp：633 项断言通过；
- Terrias：架构检查与 282 项 C# 断言通过；
- shared write、content/tool/shared、RPC authority 与架构边界门禁通过；
- Terrias、SanGuoShaExp、AuraToolsExp 三个消费者均以 0 警告、0 错误构建；
- 完整共享发布矩阵与 DLL 打包检查通过；
- 构建产物与三个打包副本 SHA-256 一致：`1036EC3EC2BE12605CFFA1ACA832E929CC16503535BE3854700B07BA39B32E4E`。

## 9. 下一轮建议

AudioArbiter 下一轮适合进入最后一组组件瘦身：

1. 提取 Hook Adapter，集中注册 Hook、解析 `ModHookContext` 并生成领域请求；
2. 提取 combat/status/career/battle result 的游戏对象读取适配器；
3. 将 `SoundProviderHandle` 与 `FileSoundProvider` 的反射、文件加载和资源生命周期移入 Provider Adapter；
4. 保持 Runtime 只负责初始化、请求编排、策略委派和 Coroutine 启动；
5. 完成后再评估 AudioArbiter 目录整理，并进入 BattleBgmArbiterShared 拆分。
