# AudioArbiter Provider Adapter 与 File Loader 第十五轮开发记录

> 日期：2026-07-18  
> 前置工作：第十一至十四轮已拆出 Contracts、Manifest、Provider Policy/Resolver、Presentation、Unity Playback、Network Runtime 与 Session State  
> 范围：反射 Provider 适配、文件音频加载、UnityWebRequest runner、扩展名策略、测试与发布护栏

## 1. 本轮目标

本轮关闭 `AudioArbiterRuntime.cs` 尾部仍然混合的 Provider 与文件资源生命周期职责：

1. 将第三方 Provider 的反射属性、方法调用和 dispose 兼容迁入专用 adapter；
2. 将公开 `FileSoundProvider` 及其 Unity Coroutine runner 迁入独立文件；
3. 将音频扩展名分类提取为可独立测试的纯策略；
4. 保持 Provider 类型全名、公开构造函数、属性和协议语义不变；
5. 防止 Runtime 重新持有 UnityWebRequest、Provider runner 或反射 clip 读取。

## 2. AudioProviderAdapter

新增 `AudioProviderAdapter.cs`，承接原 Runtime 内部的 `SoundProviderHandle` 和 `ResolvedSound`：

- 读取 `ProviderId`、`OwnerModId`、优先级、Bus、Policy、Sync、Cooldown、Gain 等属性；
- Owner 为空时继续使用 Provider 程序集名作为兼容回退；
- 通过 `AudioProviderResolver.QualifyProviderId` 生成 owner-qualified identity；
- 将增益转换为实际音量 multiplier；
- 解析 narration/vocal suppression 列表；
- 反射调用 `Evaluate`、`GetLoadState`、零参数或单参数 `GetClip`；
- 兼容 `IDisposable` 与公开 `Dispose()` 方法；
- 将 Provider 与已解析 `AudioClip` 组合为 `ResolvedSound`。

该 adapter 可以依赖 `AudioClip` 和 Unity 数学/日志，因为它是游戏资源适配层；但不得拥有文件传输、Coroutine、Hook 或 RPC。

## 3. AudioFileSoundProvider

公开 `FileSoundProvider` 已从 Runtime 迁移到 `AudioFileSoundProvider.cs`，其完整类型名仍为 `AudioArbiter.Shared.FileSoundProvider`。

专用文件现在集中负责：

- Provider 元数据和 manifest condition；
- `AudioProvider.<owner>.<provider>` 持久 runner GameObject；
- `UnityWebRequestMultimedia.GetAudioClip` 加载；
- WAV、OGG、MPEG AudioType 选择；
- generation 校验，拒绝 dispose 或重载后的迟到 completion；
- missing、unsupported、loading、failed、ready、disposed 状态；
- runner Coroutine 停止和 GameObject 销毁；
- 加载完成后的 clip 命名和诊断。

`ProviderRunner` 仍是 `FileSoundProvider` 私有的 MonoBehaviour，仅作为 Coroutine driver，不参与 Hook、RPC、仲裁或播放编排。

## 4. AudioFileLoadPolicy

新增无 Unity 依赖的 `AudioFileLoadPolicy.cs`：

- `.wav` → WAV；
- `.ogg` → OGG Vorbis；
- `.mp3`、`.m4a`、`.aac` 和未知扩展 → 保持既有 MPEG fallback；
- `.mp4`、`.m4v`、`.mov` → `UnsupportedVideoContainer`。

`FileSoundProvider` 在创建 UnityWebRequest 前先调用该策略，因此 video container 的拒绝边界保持明确且可测试。

## 5. Runtime 收敛结果

本轮 `AudioArbiterRuntime.cs` 从 2057 行降至 1596 行，减少 461 行；相对最初约 3276 行累计减少约 1680 行，超过一半。

新增文件规模：

- `AudioFileLoadPolicy.cs`：34 行；
- `AudioProviderAdapter.cs`：247 行；
- `AudioFileSoundProvider.cs`：224 行。

Runtime 不再包含：

- `SoundProviderHandle` 类型实现；
- `ResolvedSound` struct；
- `FileSoundProvider` 实现；
- `ProviderRunner` MonoBehaviour；
- UnityWebRequest 音频下载；
- 文件扩展名到 AudioType 的判断；
- Provider 的 `GetClip`/`Dispose` 反射代码。

Runtime 仍负责 Provider 注册列表、仲裁调用、请求编排、Hook 和播放 Coroutine；资源适配与加载生命周期已经独立。

## 6. 测试与门禁

`AudioArbiterShared.Tests` 从 282 项增至 292 项，新增覆盖：

- WAV 与大小写扩展分类；
- OGG Vorbis 分类；
- MP3、M4A、AAC 的 MPEG 兼容路径；
- 未知或空扩展继续使用 MPEG fallback；
- MP4、M4V、MOV 视频容器拒绝。

架构门禁新增要求：

- 文件分类策略不得依赖 Unity、Witch、Hook 或 transport；
- `SoundProviderHandle` 必须留在 Provider Adapter；
- Provider Adapter 必须通过 `AudioPropertyReader` 读取属性并拥有 `GetClip` 反射；
- Provider Adapter 不得持有 UnityWebRequest、Coroutine、network 或 Hook；
- `FileSoundProvider` 必须拥有 Unity audio loading、runner 与 generation guard；
- File Provider 不得持有 Hook 或 RPC；
- Runtime 不得恢复 Provider 类型声明、runner、UnityWebRequest、DownloadHandlerAudioClip、反射 GetClip 或 ResolvedSound。

AuraTools 架构测试也改为验证这些专用文件，而不是依赖 Provider 实现仍位于 Runtime。

## 7. 兼容性

本轮未修改：

- `AudioArbiter.Shared.FileSoundProvider` 类型全名；
- 公开构造函数、属性与方法；
- `CurrentBuildId = audio-arbiter-2026-07-11-v8`；
- ProtocolVersion 6 与 minimum protocol；
- Provider owner identity、优先级、hard claim、cooldown、gain 和 suppression 语义；
- 文件扩展名与 AudioType 映射；
- generation/dispose 行为和日志状态。

1228 项公共 API 基线完全一致，因此无需提升 BuildId 或 ProtocolVersion。

## 8. 完整验证结果

- AudioArbiterShared：292 项断言通过；
- AuraCgShared：153 项断言通过；
- AuraSharedCore：92 项断言通过；
- Aura.Shared：1228 项公共 API 兼容基线通过；
- AuraDirector：20 项断言通过；
- AuraToolsExp：634 项断言通过；
- SunExp：架构检查与 282 项 C# 断言通过；
- shared write、content/tool/shared、RPC authority 与架构边界门禁通过；
- SunExp、SanGuoShaExp、AuraToolsExp 三个消费者均以 0 警告、0 错误构建；
- 完整共享发布矩阵与 DLL 打包检查通过；
- 构建产物与三个打包副本 SHA-256 一致：`3BB43F7B1C22966B0BB0036B7DADEF43438B4E54EE0AD4BFD994DE597E39FAA1`。

## 9. 下一轮建议

AudioArbiter 的下一轮应集中处理 Hook/Game Adapter：

1. 提取 Hook 注册表和 before/after 安装适配器；
2. 将 `ModHookContext`、`IScriptExecutor`、`StatusManager` 转换为 `SoundPlaybackRequest` 的逻辑移入请求工厂；
3. 提取 career、status role/id、HP ratio、local ownership 和数据字段读取；
4. 保持 Coroutine、Unity replacement pairing 与请求编排继续由唯一 Component 驱动；
5. Hook 边界稳定后，评估 AudioArbiter 是否达到“Runtime 仅初始化、编排和委派”的验收目标，再进入 BattleBgmArbiterShared。
