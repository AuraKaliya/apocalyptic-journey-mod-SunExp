# AudioArbiter Contracts 与测试护栏第十一轮开发记录

> 日期：2026-07-18  
> 前置工作：AuraCg 播放、媒体、Overlay、Network、Provider 与注册请求解析边界已完成  
> 范围：Audio Manifest/Request 契约、反射读取器、RPC 契约、网络事件映射、独立行为测试与发布护栏

## 1. 本轮目标

本轮启动 `AudioArbiterShared` 拆分，但不提前修改 Provider 仲裁、播放、原生音效替换、Hook 或网络权限语义。目标是先形成后续拆分所需的稳定契约层：

1. 将 Manifest DTO、事件常量和 `SoundPlaybackRequest` 移出 3276 行 Runtime；
2. 将 RPC 命令与传输副本映射移出 Runtime；
3. 将异构 Provider/Request 的公共属性反射读取集中到独立适配器；
4. 建立不依赖 Unity/Witch 运行时的 Audio 行为测试；
5. 通过公共 API、架构、RPC authority、消费者构建和 DLL 哈希门禁证明行为兼容。

## 2. 新增边界

### 2.1 AudioContracts

`AudioContracts.cs` 现在集中保存：

- `AudioRegistryManifest`、`AudioProtocolManifest`、`AudioRegistryDefaults`；
- `AudioProviderManifest`、`AudioProviderMatch`、`AudioSuppressOriginal`；
- `SoundEventKinds`、`SoundBuses`、`SoundPolicies`；
- `SoundPlaybackRequest` 及其异构对象投影入口。

所有 public 类型全名、字段、属性、常量、序列化标记和默认值保持不变。`SoundPlaybackRequest.FromObject` 继续只投影既有字段，不推断 `IsRemote`，不复制本地 `ModConfig`，也不自行关闭同步。

### 2.2 AudioPropertyReader

`AudioPropertyReader.cs` 接管原 Runtime 内部 `PropertyReader`：

- 仅读取 public instance property；
- 保留 string/int/long/bool/float 的原转换规则；
- 缺失属性、空对象、解析失败和 getter 异常继续返回调用方 fallback；
- 不依赖 Unity、Witch、Hook 或游戏 Manager。

`AudioArbiterRuntime.ReadString/ReadInt/ReadLong/ReadFloat/ReadBool` 公共入口仍然存在，并委派给新读取器，因此消费者 API 不变。

### 2.3 AudioNetworkContracts 与 AudioNetworkEventMapper

`AudioNetworkContracts.cs` 接管：

- `RpcAudioEvent`；
- `IAudioArbiterServerBoundRpcCommand`；
- `RpcAudioPresentationRequest`；
- `RpcAudioFightSession`。

`AudioNetworkEventMapper.cs` 只负责构造传输副本。它精确保留原字段集合，强制 `DisableSync = true`，不复制 `ModConfig` 和接收端 `IsRemote` 状态。RPC 的 sender binding、LobbyHost 校验、远端执行入口和 fight session 应用语义未改变。

## 3. Runtime 收敛结果

`AudioArbiterRuntime.cs` 从 3276 行降至 2867 行，减少 409 行。新增文件规模：

- `AudioContracts.cs`：194 行；
- `AudioPropertyReader.cs`：99 行；
- `AudioNetworkContracts.cs`：105 行；
- `AudioNetworkEventMapper.cs`：35 行。

Runtime 本轮仍保留 Provider 列表/解析、播放、原生替换、Hook、网络会话和低血量状态；这些是后续轮次的拆分对象。本轮只建立可测试的契约地基，不宣称 Audio 拆分已经完成。

## 4. 测试与门禁

新增 `AudioArbiterShared.Tests`，包含 112 项行为断言，覆盖：

- Manifest 与 Provider DTO 默认值；
- 事件种类、Bus、播放策略和展示最大时效常量；
- 属性读取的类型值、字符串转换、空值、无效值和异常 getter fallback；
- `SoundPlaybackRequest.FromObject` 的完整字段投影；
- typed request 保持引用不变；
- 网络副本字段、独立对象、`DisableSync`、`IsRemote` 与 `ModConfig` 边界。

新增 `tools/Test-AudioArbiterShared.ps1`，并将 `audioarbiter-behavior` 加入共享发布矩阵。共享架构扫描和兼容基线同时新增文件归属与纯逻辑依赖门禁，防止契约、RPC 映射和反射读取回流到 Runtime。

## 5. 兼容性

本轮未修改：

- `AudioArbiter.Shared.AudioArbiterRuntime+AudioArbiterComponent` 完整类型名；
- `CurrentBuildId = audio-arbiter-2026-07-11-v8`；
- `CurrentProtocolVersion = 6` 和 minimum protocol；
- public 类型、方法、字段、属性、常量与序列化形状；
- Provider identity、匹配、优先级和 cooldown 语义；
- sender authority、fight session、duplicate suppression；
- Hook、播放、AudioSource 和原生音效替换行为。

因此无需提升 BuildId 或 ProtocolVersion。

## 6. 完整验证结果

- AudioArbiterShared：112 项断言通过；
- AuraCgShared：153 项断言通过；
- AuraSharedCore：92 项断言通过；
- Aura.Shared：1228 项公共 API 兼容基线通过；
- AuraDirector：20 项断言通过；
- AuraToolsExp：632 项断言通过；
- SunExp：架构检查与 282 项 C# 断言通过；
- shared write、content/tool/shared、RPC authority 与架构边界门禁通过；
- SunExp、SanGuoShaExp、AuraToolsExp 三个消费者均以 0 警告、0 错误构建；
- 完整共享发布矩阵与 DLL 打包检查通过；
- 构建产物和三个打包副本 SHA-256 一致：`70CE7029F890581AC908801C09834D6686CF95495EEDF39F50A9416A1461E810`。

## 7. 下一轮建议

下一轮应继续遵循既定顺序，拆分 Manifest Loader 与 Provider Resolver：

1. 提取 manifest 路径解析、JSON 读取、schema/protocol 校验和默认值合并；
2. 提取 owner-qualified Provider identity、显式 Provider 匹配和确定性排序；
3. 提取 cooldown/适用性等无 Unity 依赖的仲裁策略；
4. 为 owner 严格匹配、远端 fail-closed、优先级、hard claim 和 cooldown 增加行为测试；
5. 暂不移动 AudioClip 加载、AudioSource 播放、原生替换和 Hook Adapter，待纯策略稳定后再拆。
