# 运行时仲裁器与扩展点

本工作区有几类不能只靠普通 CSV 表描述的运行时扩展层。它们应当被当作共享服务：从 MOD 入口初始化，然后注册 provider 或读取 manifest。

## 音效

来源文件：

- `AudioArbiterShared/AudioArbiterRuntime.cs`
- `SunExp-Dev/GameApi/AudioApi.cs`
- `SunExp/audio.registry.json`
- `CardUseCialloExp-Dev/Hooks/CardUseSoundRuntime.cs`

`AudioArbiterRuntime` 会创建全局 Unity 对象 `AudioArbiter.Global`。MOD 可以直接用 `RegisterSoundProvider(...)` 注册 provider，也可以用 `RegisterManifest(...)` 读取 manifest。

SunExp 当前使用的 manifest 字段：

- `schemaVersion`、`ownerModId`
- `audioProtocol.minVersion`、`audioProtocol.preferredVersion`
- `defaults.sync`、`defaults.hardClaim`、`defaults.cooldownSeconds`、`defaults.gainDb`、`defaults.volumeMultiplier`
- `providers[].providerId`、`kind`、`bus`、`policy`、`priority`、`path`
- `providers[].match`：角色/职业过滤、战斗结果过滤、血量阈值过滤、本地拥有者限制
- `providers[].suppressOriginal`：原版语音或旁白抑制规则

已知音效 kind 包括 `CardUse`、`CareerSelected`、`LowHealth`、`BattleCompleted` 等内置事件。SunExp 还使用 `SunExp.Wuna.WhiteSunPrayer`、`SunExp.Wuna.GraveSong` 这样的自定义 kind，并通过 `AudioApi.PlayWhiteSunPrayer()`、`AudioApi.PlayGraveSong()` 触发。

`CardUseCialloExp` 是最小的直接 provider 示例：它为 `SoundEventKinds.CardUse` 注册 `FileSoundProvider`，走 `Effect` bus，替换原音效，硬认领请求，并同步到远端。

## 战斗 BGM

来源文件：

- `BattleBgmArbiterShared/BattleBgmArbiterRuntime.cs`
- `SunExp-Dev/GameApi/BattleBgmProviderRuntime.cs`
- `BackgroundAudioReplaceExp-Dev/Hooks/BackgroundBattleMusicRuntime.cs`

`BattleBgmArbiterRuntime` 会创建全局 Unity 对象 `BattleBgmArbiter.Global`。BGM provider 通过 `RegisterProvider(...)` 注册，可以匹配冒险上下文、战斗上下文，或同时匹配两者。

SunExp 从 `audio.registry.json` 读取 BGM 定义：

- `battleBgmDefaults.priority`
- `battleBgmDefaults.hardClaim`
- `battleBgmDefaults.silenceWhenLoading`
- `battleBgmDefaults.fallbackToOriginalWhenFailed`
- `battleBgmDefaults.allowMidBattleSwitch`
- `battleBgmProviders[]`

`BackgroundAudioReplaceExp` 是最小的直接 provider 示例：它注册一个由 `BGM.mp3` 驱动的 `FileBattleBgmProvider`，硬认领战斗音乐，加载时静音，加载失败时回退原版音乐。

## 技能 CG 覆盖层

来源文件：

- `SkillCGExp-Dev/Hooks/SkillCgRuntime.cs`
- `SkillCGExp-Dev/Hooks/SkillCgArbiterRuntime.cs`
- `SkillCGExp-Dev/Config/SkillCgConfig.cs`
- `SkillCGExp/SkillCGConfig.json`

`SkillCGExp` 是覆盖层仲裁器，不是音频仲裁器。它监听技能牌使用请求，解析最高优先级的启用规则，并按配置显示图片和淡入、停留、淡出时间。运行时全局对象是 `SkillCGExp.CgArbiter.Global`。

当前 config rule 支持：

- `enabled`
- `providerId`
- `cardId`
- `action`
- `ownerInstanceId`
- `image`
- `priority`
- `fadeIn`、`hold`、`fadeOut`

如果 `SkillCGConfig.json` 缺失或为空，`SkillCGExp` 会回退到内置官方角色技能 CG 规则。

## 编写规则

当多个 MOD 可能触碰同一类音效、BGM 或覆盖层表面时，优先接入这些共享仲裁器。直接替换 Unity 音频或 UI 状态的私有 hook，要么改为仲裁器 provider，要么在文档中说明为什么必须绕过共享服务。
