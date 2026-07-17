# AudioArbiter Manifest 与 Provider Resolver 第十二轮开发记录

> 日期：2026-07-18  
> 前置工作：第十一轮已拆出 Audio Contracts、RPC Contracts、网络映射与属性读取器  
> 范围：Manifest 加载/校验/默认值规划、Manifest Match Policy、Provider identity/仲裁/cooldown、测试与发布护栏

## 1. 本轮目标

继续收敛 `AudioArbiterRuntime.cs`，优先提取无 Unity/Witch 对象依赖的领域规则：

1. 将 manifest 文件定位、JSON 反序列化、schema/protocol 校验和 owner 规范化移出 Runtime；
2. 将 provider 默认值合并、资源路径规划和 request match 条件移出 Runtime；
3. 将 owner-qualified identity、严格/兼容匹配、远端 fail-closed、优先级和 hard claim 仲裁移出 Runtime；
4. 将 cooldown key 与获取规则改为独立纯策略；
5. 保持 `AudioClip` 获取、Provider 反射适配、播放和 Hook 在现有 Unity 适配层。

## 2. AudioManifestLoader

新增 `AudioManifestLoader.cs`，集中负责：

- 默认 `audio.registry.json` 与自定义 manifest 路径解析；
- 文件存在性和读取；
- Newtonsoft.Json 优先、Unity JsonUtility 回退的既有反序列化顺序；
- 非正 schema 归一为 1；
- 拒绝高于 `SupportedManifestSchemaVersion` 的 schema；
- 拒绝高于当前 Runtime 的 `audioProtocol.minVersion`；
- manifest owner 回退与 trim；
- provider/default 合并为 `AudioManifestProviderPlan`；
- `Shared:`、绝对路径与 MOD 相对路径解析。

Runtime 只处理加载结果、告警和 `FileSoundProvider` 实例化，不再自己解释 manifest 协议。

## 3. AudioManifestMatchPolicy

新增 `AudioManifestMatchPolicy.cs`，接管 manifest provider 的适用条件：

- kind 与 vocal state；
- career/role 的前后缀兼容匹配；
- card、buff、effect、action 与 battle result；
- `localOwnerOnly` 的既有本地/远端语义；
- `hpRatioCrossDown` 的阈值穿越判断。

该策略只依赖契约与 `AudioPropertyReader`，不依赖 Unity、游戏 Manager、Hook 或具体 Provider。

## 4. AudioProviderResolver

新增 `AudioProviderResolver.cs`，使用泛型资源候选接口隔离具体 `AudioClip`，集中负责：

- owner-qualified provider identity；
- bare ID 与 qualified ID 匹配；
- owner-scoped 严格匹配；
- 本地 owner mismatch 的旧 bare-ID 兼容回退；
- 远端 owner mismatch 和 qualified mismatch 的 fail-closed；
- Provider 条件过滤；
- load state 与资源可用性；
- hard claim 对后续 Provider 的阻断；
- priority 降序与 qualified identity 稳定排序；
- 结构化 resolution status 和远端 mismatch 诊断标志。

`SoundProviderHandle` 继续封装 Provider 反射和 `AudioClip` 获取，但其 identity、match 和排序规则全部委派给 Resolver。

同文件新增 `AudioProviderCooldownPolicy`，保持原 key：

`qualifiedProviderId | kind | roleId | statusInstanceId`

过期边界仍为 `now < until` 时拒绝，等于截止时间时允许；零 cooldown 不保留状态。

## 5. Runtime 收敛结果

本轮 `AudioArbiterRuntime.cs` 从 2867 行降至 2586 行，减少 281 行；相对拆分前的 3276 行累计减少 690 行。

新增模块规模：

- `AudioManifestLoader.cs`：257 行；
- `AudioManifestMatchPolicy.cs`：118 行；
- `AudioProviderResolver.cs`：293 行。

Runtime 目前仍保留：

- Provider 反射 Handle；
- AudioClip/FileSoundProvider 加载；
- AudioSource/AudioManager 播放；
- 原生音效替换与抑制；
- Hook Adapter；
- 网络会话、fight claim 与低血量运行状态。

## 6. 测试与门禁

`AudioArbiterShared.Tests` 从 112 项增加到 183 项，新增覆盖：

- manifest 默认/自定义路径；
- 文件缺失、无效 JSON、schema 和 protocol 拒绝；
- owner 回退及规范化；
- provider/default 合并；
- Shared/相对资源路径；
- 完整 manifest request match 与阈值穿越；
- qualified identity、bare identity 和稳定排序；
- owner 严格匹配与本地兼容回退；
- 远端和 qualified mismatch fail-closed；
- soft unavailable fallback 与 hard claim 阻断；
- cooldown 获取、作用域、到期边界和零状态保留。

共享架构门禁新增要求：

- Manifest 加载/校验必须留在 `AudioManifestLoader`；
- Manifest 条件必须留在 `AudioManifestMatchPolicy`；
- identity/仲裁/cooldown 必须留在 `AudioProviderResolver`；
- 三个模块不得依赖 Unity 对象、游戏 Manager、Hook 或具体 `FileSoundProvider`；
- Runtime 不得恢复旧反序列化、条件构建、内联 resolver 或私有 identity 实现。

`Test-SunExpCSharp.ps1` 原先固定扫描 Runtime 内旧 matcher 调用，现已改为验证 Resolver 的 owner-strict/legacy/fail-closed 契约及 Runtime 的诊断委派，文件拆分不再被旧路径绑定。

## 7. 兼容性

本轮未修改：

- `AudioArbiter.Shared.AudioArbiterRuntime+AudioArbiterComponent` 完整类型名；
- `CurrentBuildId = audio-arbiter-2026-07-11-v8`；
- `CurrentProtocolVersion = 6` 与 minimum protocol；
- public API 与序列化形状；
- Provider identity 和匹配语义；
- hard claim、cooldown、远端 fail-closed 与本地兼容回退；
- RPC authority、fight dedupe、播放和 Hook 行为。

因此无需提升 BuildId 或 ProtocolVersion。

## 8. 完整验证结果

- AudioArbiterShared：183 项断言通过；
- AuraCgShared：153 项断言通过；
- AuraSharedCore：92 项断言通过；
- Aura.Shared：1228 项公共 API 兼容基线通过；
- AuraDirector：20 项断言通过；
- AuraToolsExp：632 项断言通过；
- SunExp：架构检查与 282 项 C# 断言通过；
- shared write、content/tool/shared、RPC authority 与架构边界门禁通过；
- SunExp、SanGuoShaExp、AuraToolsExp 三个消费者均以 0 警告、0 错误构建；
- 完整共享发布矩阵和 DLL 打包检查通过；
- 构建产物和三个打包副本 SHA-256 一致：`CE8C5666869D56D509FDF537BAA649AFD423E855D8AC4EE5B532A5E4F2B4B614`。

## 9. 下一轮建议

下一轮进入 Playback Service 与 Presentation Policy：

1. 提取播放 Bus/Policy 判定和 replacement decision；
2. 提取 PendingReplacement 状态机与远端 fallback pairing；
3. 提取 suppression planning，但将实际 AudioManager/AudioSource 调用保留在 Unity adapter；
4. 为 additive、replace、replace-original、suppress-original、remote pairing 和 late-original suppression 增加纯行为测试；
5. 再评估 Provider reflection adapter 是否可独立成文件，避免与播放拆分同时扩大风险面。
