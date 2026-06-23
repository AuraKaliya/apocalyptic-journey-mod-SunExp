# Audio Shared Layer Implementation Notes

本文整理共享层 `AuraAudioShared` / `AudioArbiterShared` 如何接入游戏音频系统，并说明它如何替换或叠加角色技能语音、出牌动作音效。

## 目标

共享层要解决的问题不是“把某个 Mod 的声音硬编码进游戏”，而是把声音声明成可注册、可匹配、可复用的 provider：

- 声音文件由资源包安装到共享目录。
- 声音元数据由 `audio.registry.json` 声明。
- 运行期通过统一的 `SoundPlaybackRequest` 匹配 provider。
- provider 决定播放通道、优先级、冷却、同步策略、原声替换策略。

这样一个角色包可以把语音、皮肤、旅程等资源作为同一个 `RolePack` 发布，同时避免多个 Mod 互相覆盖同一份资源。

## 初始化链路

SunExp 的入口在 `SunExp-Dev/Entry.cs`：

```text
Entry.Initialize
  -> AudioApi.Initialize
    -> AuraAudioRuntime.Initialize
      -> AuraSharedRuntime.Initialize
      -> AuraSharedPackageEngine.InstallManifest
      -> AudioArbiterRuntime.Initialize
      -> AudioArbiterRuntime.RegisterManifest
```

关键文件：

- `SunExp-Dev/Entry.cs`
- `SunExp-Dev/GameApi/AudioApi.cs`
- `AuraAudioShared/AuraAudioRuntime.cs`
- `AudioArbiterShared/AudioArbiterRuntime.cs`
- `SunExp/SharedResources/package.json`
- `SunExp/audio.registry.json`

`SharedResources/package.json` 声明资源安装，例如把：

```text
SunExp/SharedResources/Audio/WuNa
```

安装为共享资源：

```text
Audio/SunExp/WuNa
```

`audio.registry.json` 再使用 `Shared:` 前缀引用共享路径：

```json
"path": "Shared:Audio/SunExp/WuNa/wuna_white_sun_prayer.wav"
```

`AudioArbiterRuntime.ResolveManifestPath` 会把 `Shared:` 转成 `AuraSharedPaths.ResolveSharedPath(...)`，普通相对路径则相对于 Mod 根目录解析。

## 全局仲裁器

`AudioArbiterRuntime.Initialize` 会创建或复用一个全局对象：

```text
AudioArbiter.Global
```

这个对象挂载 `AudioArbiterComponent`，负责：

- 保存 provider 列表。
- 按优先级排序 provider。
- 注册游戏方法 Hook。
- 解析播放请求。
- 控制冷却。
- 播放 effect / vocal。
- 在联机中把音频事件同步给其他玩家。

如果已有全局对象存在，后续 Mod 会通过反射检查协议版本、BuildId 和公开方法形状。兼容就复用，不兼容就禁用当前消费者的共享音频功能，避免影响其他初始化步骤。

## Provider 注册

`audio.registry.json` 的每一项 provider 会被转换成 `FileSoundProvider`，再交给 `RegisterSoundProvider`：

```json
{
  "providerId": "SunExp.Wuna.WhiteSunPrayer",
  "kind": "SunExp.Wuna.WhiteSunPrayer",
  "bus": "Vocal",
  "policy": "Additive",
  "priority": 120,
  "path": "Shared:Audio/SunExp/WuNa/wuna_white_sun_prayer.wav",
  "gainDb": 20,
  "cooldownSeconds": 0.2,
  "match": {
    "careerIds": ["wuna", "SunExp_wuna_wuna"],
    "roleIds": ["wuna", "SunExp_wuna_wuna"]
  }
}
```

主要字段含义：

- `providerId`: provider 唯一标识。重复注册会替换旧 provider。
- `kind`: 事件类型。必须匹配 `SoundPlaybackRequest.Kind`。
- `bus`: 播放通道，目前核心是 `Effect` 和 `Vocal`。
- `policy`: 播放策略，支持 `Additive`、`Replace`、`ReplaceOriginal`、`SuppressOriginal`。
- `priority`: 多个 provider 命中时，高优先级先尝试。
- `hardClaim`: 命中但资源未就绪时是否阻止低优先级 provider 兜底。
- `sync`: 是否通过联机 RPC 同步到其他玩家。
- `gainDb` / `volumeMultiplier`: 最终音量倍数。
- `cooldownSeconds`: 同一 provider / kind / role / status 的最小触发间隔。
- `match`: 细分匹配条件。
- `suppressOriginal`: 用于压制本体旁白或原始语音状态。

`FileSoundProvider` 会异步加载音频文件，支持常见音频容器；视频容器如 `.mp4`、`.mov` 会被拒绝并提示导出为音频文件。

## 请求解析

所有播放都归一为 `SoundPlaybackRequest`。核心字段包括：

- `Kind`
- `CareerId`
- `RoleId`
- `StatusInstanceId`
- `CardId`
- `BuffId`
- `EffectName`
- `ActionName`
- `VocalState`
- `BattleResult`
- `Hp` / `MaxHp` / `PreviousHpRatio` / `HpRatio`
- `IsLocalOwner`

`AudioArbiterComponent.Resolve` 会按 provider 列表顺序查找：

1. 若请求指定 `ProviderId`，先过滤 provider。
2. 调用 provider 的 `Evaluate(request)`。
3. 检查加载状态是否为 `Ready`。
4. 取出 `AudioClip`。
5. 返回第一个可用结果。

如果 provider 设置了 `hardClaim`，并且它已命中但资源缺失或未就绪，则不会继续使用后面的 provider 兜底。

## 技能语音实现

SunExp 目前的两个主动技能语音是脚本主动请求，不是通过替换游戏本体的 `VocalState.Skill` 完成。

链路如下：

```text
WunaScripts.UseWhiteSunPrayer
  -> AudioApi.PlayWhiteSunPrayer
    -> AudioArbiterRuntime.RequestSound(kind: SunExp.Wuna.WhiteSunPrayer)
      -> registry provider
        -> bus: Vocal
          -> AudioManager.PlayVocal(roleId, clip)
```

`UseGraveSong` 同理，使用 `SunExp.Wuna.GraveSong`。

这种方式的优点是触发点准确，能与技能逻辑同事务发生；缺点是不会自动覆盖所有游戏本体的 `Skill` 语音。若要替换原生 `VocalState.Skill`，应注册 `kind: "VocalState"` 且 `vocalState: "Skill"` 的 provider，并按 `careerIds` / `roleIds` / `cardIds` 做匹配。

## 出牌音效替换实现

共享层已实现通用出牌音效替换，但当前 `SunExp/audio.registry.json` 还没有实际注册 `kind: "CardUse"` 的 provider，因此 SunExp 现状是“能力已存在，当前角色包尚未启用普通出牌音效替换”。

替换流程如下：

```text
FightUI.CallActionAnimation.Before
  -> RequestSound(kind: CardUse, card/effect/action context)
    -> 命中 bus: Effect + Replace/ReplaceOriginal/SuppressOriginal provider
      -> 设置 pendingReplacement
        -> EffectSound.Start.Before
          -> 替换、延迟播放或清空 EffectSound.clip
```

为什么不是直接 Hook `AudioManager.PlayEffect`？

游戏里大量 UI、回合、抽牌、增益、死亡等都会调用 `AudioManager.PlayEffect`。如果在这里全局拦截，容易误伤无关音效。共享层选择在 `FightUI.CallActionAnimation` 之前捕获“这一次卡牌动作”的上下文，再只给下一次 `EffectSound.Start` 开一个短暂替换窗口。

`pendingReplacement` 的窗口目前是 1 秒，且默认只消费 1 次。这与游戏本体的动作特效播放节奏相匹配，可以尽量把替换限定在本次出牌动作里。

替换策略：

- `Replace` / `ReplaceOriginal`: 将 `EffectSound.clip` 换成 provider 的 clip。
- `SuppressOriginal`: 将 `EffectSound.clip` 置空。
- 当 `volumeMultiplier` 不是 1 时，为了保留自定义音量，先置空原 clip，再按原 `EffectSound.delay` 启动协程播放新 clip。

## 原声压制

对于旁白或状态语音，provider 可以声明：

```json
"suppressOriginal": {
  "vocalStates": ["Dying"],
  "narrationIds": [17, 18]
}
```

当前实现中，`suppressNarrationIds` 会在 provider 命中时写入短期压制表，然后 `NarrationManager.Play.After` 观察并停止对应旁白的 vocal source。低血量语音就是这种模式：自定义语音播放后，短时间内压制本体濒死旁白。

## 联机同步

provider 默认 `sync: true`。本地请求命中后，`SyncRemote` 会通过：

```text
PlayerManager.SendRpcCommandExcludeOwner(new RpcAudioEvent(request))
```

把事件发给其他玩家。远端收到后用同一套 provider 解析并播放，但请求会标记为 `IsRemote`，避免重复广播。

需要注意：

- 音频本身不通过网络传输，所有客户端都需要安装同一资源包。
- `localOwnerOnly` 的匹配会避免非本地拥有者触发某些个人语音。
- `DisableSync` 可用于显式禁止某次请求同步。

## 新增一个出牌替换 provider 的模板

如果要给某张牌替换出牌动作音效，可以在 `SunExp/audio.registry.json` 中增加类似配置：

```json
{
  "providerId": "SunExp.Wuna.SomeCardUse",
  "kind": "CardUse",
  "bus": "Effect",
  "policy": "ReplaceOriginal",
  "priority": 150,
  "path": "Shared:Audio/SunExp/WuNa/some_card_use.wav",
  "cooldownSeconds": 0.05,
  "match": {
    "careerIds": ["wuna", "SunExp_wuna_wuna"],
    "cardIds": ["SunExp_sunexp_some_card"],
    "actionNames": ["Skill"]
  }
}
```

匹配条件可以按需要换成 `effectNames`、`actionNames` 或仅角色级匹配。角色级匹配范围更大，卡牌级匹配更安全。

## 当前 SunExp 状态

当前已启用的音频 provider：

- 选择角色语音：`CareerSelected`
- 低血量语音：`LowHealth`
- 白曜圣祷主动技语音：`SunExp.Wuna.WhiteSunPrayer`
- 圣庭墓曲主动技语音：`SunExp.Wuna.GraveSong`
- 战斗胜利语音：`BattleCompleted`

当前未启用但共享层支持的能力：

- 普通出牌 `CardUse` 音效替换。
- 原生 `VocalState.Skill` 技能语音替换。

## 维护建议

- 新语音优先走 `audio.registry.json`，避免在代码中写死音频路径。
- 角色技能语音若与技能逻辑强绑定，保留 `AudioApi` 主动请求最清晰。
- 普通出牌音效优先用 `CardUse` provider，通过 `cardIds` 精确匹配。
- 涉及本体原声压制时，优先使用短窗口压制，不要全局禁用 `AudioManager`。
- 修改音频资源后同时检查 `SharedResources/package.json`、`audio.registry.json` 和对应 DLL 初始化链路。
