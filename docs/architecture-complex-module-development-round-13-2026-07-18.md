# AudioArbiter Playback 与 Presentation 第十三轮开发记录

> 日期：2026-07-18  
> 前置工作：第十一、十二轮已拆出 Contracts、Manifest、Match Policy 与 Provider Resolver  
> 范围：播放路由、原生音效 replacement 决策、pending/pairing 状态、suppression policy、Unity Playback Adapter、测试与发布护栏

## 1. 本轮目标

继续将 `AudioArbiterRuntime.cs` 中的播放领域规则与 Unity 执行细节分离：

1. 提取 Effect/Vocal、Additive/Replace/Suppress 的路由与决策；
2. 提取 pending replacement、远端 event pairing 和 fallback suppression tail 状态；
3. 提取 narration suppression 的时限规划与清理；
4. 将 AudioManager、AudioSource 和反射兼容调用集中到 Unity adapter；
5. 保持 Coroutine 驱动与 Hook 生命周期仍由唯一 Runtime 组件所有。

## 2. AudioPresentationPolicy

新增 `AudioPresentationPolicy.cs`，集中负责：

- 判断 Replace、ReplaceOriginal、SuppressOriginal 策略；
- 仅为 Effect Bus 的 CardUse 建立原生音效 replacement；
- 区分本地 1 秒 pairing 与远端 0.15 秒 pairing；
- 决定是否启动远端 fallback Coroutine；
- 生成 `local-pair-pending` / `remote-pair-pending` 结果；
- 解析 Vocal role id 的 status → role → career → provider fallback 顺序；
- 决定 native effect 是保持、清空、替换 clip，还是按原 delay 单独播放自定义音量；
- 维持 `0.001f` 音量恒等容差。

同文件新增 `AudioSuppressionPolicy`：

- 将 narration id 写入时限 ledger；
- 保持 `now <= until` 时仍然抑制；
- 检查结束后清理 `now > until` 的过期项；
- 不直接操作 AudioSource 或游戏对象。

## 3. AudioReplacementCoordinator

新增泛型 `AudioReplacementCoordinator<TResource>`，接管原 Runtime 内部 `PendingReplacement` 与远端 pairing 集合：

- arm pending replacement；
- 截止时间与 remaining 校验；
- native effect 消费和多次 remaining 递减；
- remote native pairing claim；
- fallback 对 pairing claim 的单次消费；
- `paired-native` 与 `fallback-original-suppressed` 结果；
- fallback 后 suppress-only tail；
- fight-start 全量清理；
- fight-session 只清理 pairing claim，保持原 pending 生命周期语义。

Coordinator 使用泛型资源，不依赖 `AudioClip`、Unity 时间或游戏 API。Runtime 显式传入当前时间，因此状态边界可独立测试。

## 4. AudioUnityPlaybackService

新增 `AudioUnityPlaybackService.cs`，集中保留必须依赖 Unity/Witch 的执行逻辑：

- `AudioManager.PlayVocal` 和 `PlayEffect`；
- 自定义音量时的 AudioSource fallback；
- `_vocalSources`、`effectSource`、mixer group 和 volume 的反射兼容读取；
- Vocal AudioSource 创建和停止。

该服务不继承 MonoBehaviour，不注册 Hook，不持有 Coroutine。远端 fallback 等待与延迟播放 Coroutine 仍由 `AudioArbiterComponent` 驱动，符合单生命周期所有者约束。

## 5. Runtime 收敛结果

本轮 `AudioArbiterRuntime.cs` 从 2586 行降至 2338 行，减少 248 行；相对拆分前的 3276 行累计减少 938 行。

新增模块规模：

- `AudioPresentationPolicy.cs`：142 行；
- `AudioReplacementCoordinator.cs`：171 行；
- `AudioUnityPlaybackService.cs`：154 行。

Runtime 不再保存或实现：

- `PendingReplacement` struct；
- `pairedRemoteReplacementIds` 集合；
- replacement policy 内联判断；
- native effect action 决策；
- AudioManager/AudioSource 播放实现；
- narration suppression ledger 算法。

Runtime 仍负责请求编排、Coroutine 启动、Hook 接入、网络转发、Provider/clip 组合和日志。

## 6. 测试与门禁

`AudioArbiterShared.Tests` 从 183 项增加到 236 项，新增覆盖：

- 本地/远端 replacement plan；
- Effect/Vocal、CardUse/SkillVoice、Additive/Replace 路由；
- Vocal role fallback；
- suppress、replace clip、自定义音量延迟播放决策；
- 音量容差；
- narration suppression 的 arm、截止边界和过期清理；
- pending 截止时间、remaining、多次消费；
- remote pairing claim 单次消费；
- native 已配对时取消 fallback；
- fallback 后 late-original suppression；
- fight/session 不同层级的状态清理。

共享架构门禁新增要求：

- Presentation/Suppression policy 必须保持无 Unity/Witch/时间源依赖；
- Replacement Coordinator 必须保持泛型、无 AudioClip/Unity 依赖；
- AudioManager 和 AudioSource 必须留在 `AudioUnityPlaybackService`；
- Unity Playback Service 不得拥有 Hook、RPC 或 Coroutine；
- Runtime 不得恢复旧 Pending struct、pairing 集合、播放反射方法或 replacement policy。

AuraTools 与 Network authority 护栏中原来扫描 `IsReplacementPolicy`、`pairedRemoteReplacementIds` 的固定实现断言，已改为验证 `AudioPresentationPolicy`、`AudioReplacementCoordinator` 和 `TryClaimPairedFallback` 契约。

## 7. 兼容性

本轮未修改：

- 全局组件完整类型名；
- `CurrentBuildId = audio-arbiter-2026-07-11-v8`；
- `CurrentProtocolVersion = 6` 与 minimum protocol；
- public API 和序列化形状；
- replacement policy、pairing 时长、音量容差；
- remote fallback、late-original suppression 和 narration suppression 行为；
- RPC authority、fight dedupe 和 Provider identity。

因此无需提升 BuildId 或 ProtocolVersion。

## 8. 完整验证结果

- AudioArbiterShared：236 项断言通过；
- AuraCgShared：153 项断言通过；
- AuraSharedCore：92 项断言通过；
- Aura.Shared：1228 项公共 API 兼容基线通过；
- AuraDirector：20 项断言通过；
- AuraToolsExp：632 项断言通过；
- SunExp：架构检查与 282 项 C# 断言通过；
- shared write、content/tool/shared、RPC authority 与架构边界门禁通过；
- SunExp、SanGuoShaExp、AuraToolsExp 三个消费者均以 0 警告、0 错误构建；
- 完整共享发布矩阵和 DLL 打包检查通过；
- 构建产物和三个打包副本 SHA-256 一致：`587EB66783DBD9BA9A17064A9DD85898A4DFBAD41181923F5923E1125B2F1DB8`。

## 9. 下一轮建议

下一轮进入 Audio Network Runtime 与 Session State：

1. 提取 fight token、play id reuse、presentation claim 和 bounded dedupe；
2. 提取本地/服务端/远端 presentation envelope 校验与 relay decision；
3. 保持 sender binding 和 PlayerManager RPC 发送在网络 adapter；
4. 为过期、重复、跨 fight、错误 sender/owner、claim 容量上限增加纯行为测试；
5. 网络边界稳定后，再进行 Hook Adapter 与 Provider reflection adapter 的最后拆分。
