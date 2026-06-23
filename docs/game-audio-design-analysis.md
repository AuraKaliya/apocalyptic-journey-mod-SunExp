# Game Audio Design Analysis

本文解析游戏本体中角色技能语音、出牌动作音效和一般音效的设计方式，重点说明共享层为什么选择 Hook 当前这些节点。

## 分层概览

游戏本体音频大致分成三层：

```text
数据层
  Data/*.csv: Effects / Action / SoundEffects / Vocal 等字段

表现调度层
  CardItem / SkillItem / FightUI / EffectManager / StatusManager

播放层
  EffectSound / AudioManager / ResourceLoader / AudioSource
```

数据层描述“要做什么动作、播什么特效”。表现调度层把卡牌、技能、状态变化转换成动画和特效调用。播放层负责把 `AudioClip` 或资源路径真正送进 Unity `AudioSource`。

## AudioManager

`AudioManager` 是游戏本体的主要播放入口。

效果音入口：

```text
AudioManager.PlayEffect(string name)
  -> ResourceLoader.Load<AudioClip>("Sounds/" + name)
  -> AudioManager.PlayEffect(AudioClip clip)
    -> effectSource.PlayOneShot(...)
```

`PlayEffect(AudioClip)` 有一个 0.1 秒重复播放抑制：同一个 clip 在极短时间内重复触发会被丢弃。它最终使用 effect 音源和 `EffectVolume` / `masterVolume` 播放。

语音入口：

```text
AudioManager.PlayVocal(string roleId, string clipPath)
  -> ResourceLoader.Load<AudioClip>(clipPath)
  -> AudioManager.PlayVocal(string roleId, AudioClip clip)
```

`PlayVocal(roleId, clip)` 按 `roleId` 维护 `_vocalSources`。同一个 `roleId` 再次播放语音时，会先 `Stop()` 旧语音，再设置新 clip 播放。这意味着角色语音天然是“同角色单声道覆盖”，而不是多个语音叠在一起。

## 角色状态语音

游戏定义了 `IStatusManager.VocalState`：

```text
FightStart
Focus
Skill
Defend
Hurt
Dying
Dead
Bored
Kill
Win
Chat
AffectionUp
```

多处战斗逻辑会调用：

```text
status.PlayVocal(IStatusManager.VocalState.xxx)
```

例如：

- 战斗开始：`FightStart`
- 受伤或格挡：`Hurt` / `Defend`
- 低血或死亡：`Dying` / `Dead`
- 主动技能：`Skill`
- 胜利：`Win`

反编译结果显示当前 `StatusManager.$Rougamo_PlayVocal` 本体为空。也就是说，`StatusManager.PlayVocal(state)` 更像是一个可被 AOP / Mod Hook 观察的语音事件点，而实际角色语音播放可能由外部切面、资源绑定或 Mod 系统补足。

这也是共享层选择 Hook `StatusManager.PlayVocal.After` 的原因：它可以稳定观察“游戏认为角色进入了某个语音状态”，再把它转成 `SoundPlaybackRequest.Kind = VocalState`。

## 主动技能流程

主动技能由 `SkillItem.TrueUse` 驱动。成功使用后，它的顺序大致是：

```text
TryUse
  -> RunScript("UseScript")
  -> self.PlayVocal(VocalState.Skill)
  -> RunScript("InitScript")
  -> DataUpdate
  -> FightUI.CallActionAnimation(scriptExecutor)
```

这里有两个可观察点：

1. `self.PlayVocal(VocalState.Skill)`：表示角色技能语音状态。
2. `FightUI.CallActionAnimation`：表示技能动作和特效即将播放。

SunExp 目前对乌娜两个主动技采用第三种方式：在 `UseScript` 对应的 C# 方法里主动调用 `AudioApi.PlayWhiteSunPrayer()` / `AudioApi.PlayGraveSong()`。这比监听 `VocalState.Skill` 更精确，因为它直接知道是哪一个技能成功通过了冷却和资源条件。

## 普通出牌流程

普通牌和攻击牌在使用成功后都会进入 `FightUI.CallActionAnimation`：

```text
CommonCardItem.UseCardDirectly
  -> RunScript("UseScript")
  -> EventTrigger("ActionAfter" + status.InstanceId)
  -> FightUI.CallActionAnimation(scriptExecutor)

AttackCardItem.TrueUse
  -> RunScript("UseScript")
  -> EventTrigger("ActionAfter" + status.InstanceId)
  -> FightUI.CallActionAnimation(scriptExecutor)
```

因此 `FightUI.CallActionAnimation` 是“卡牌已经使用成功，并且即将进入动作表现”的关键边界。

## Action 与 Effects 字段

卡牌数据表通常有这两个字段：

```text
Effects
Action
```

`FightUI.CallActionAnimation` 会读取：

- `Effects`: 指定要播放的动作特效名称。
- `Action`: 尝试解析成 `IStatusManager.AnimatedState`，例如 `Attack`、`Skill`、`Special`。

处理逻辑大致是：

```text
animationData.effectName = data["Effects"]
animationData.animationState[0] = Parse(data["Action"])

if Effects 不为空:
  EffectManager.PlayActionEffect(scriptExecutor, Effects, 0.05)
else if Action 是 Attack 或 Skill:
  播放角色默认 Attack/Skill 特效
  播放角色默认 Hit 特效

animationQueue.Enqueue(animationData)
```

也就是说，`Action` 更偏向角色动作状态，`Effects` 更偏向实际播放的特效资源。如果 `Effects` 为空，本体会根据角色自己的动画/特效配置兜底。

## EffectManager

`EffectManager` 提供两类入口：

```text
PlayEffect(scriptExecutor, effectName)
PlayActionEffect(scriptExecutor, effectName, delay)
```

`PlayEffect` 会先把效果事件入队到 `FightManager`，再立刻 `InternalPlayEffect`。`PlayActionEffect` 则等待指定延迟后调用 `InternalPlayEffect`。

`InternalPlayEffect` 会把逗号分隔的 `effectName` 拆开，逐个在 `EffectInfos` 中查找 `EffectBase`，然后按 target 类型播放到 self 或目标身上。

这层负责“哪个战斗对象播放哪个特效”，但不直接决定每个特效内部的声音怎么播。具体音效通常在特效 prefab 或组件中，由 `EffectSound` 触发。

## EffectSound

`EffectSound` 是动作特效里最适合替换音效的节点。

它只有两个关键字段：

```text
public float delay;
public AudioClip clip;
```

`Start()` 逻辑是：

```text
等待 delay 秒
  -> AudioManager.Instance.PlayEffect(clip)
```

这意味着如果在 `EffectSound.Start` 执行前修改 `clip`，就能只替换当前这个特效音效，而不影响其他 UI 音效、回合音效或系统音效。

共享层的出牌音效替换正是基于这个特性：

1. 在 `FightUI.CallActionAnimation.Before` 收集当前卡牌上下文。
2. 若命中 `CardUse` provider，则保存一个短期 `pendingReplacement`。
3. 在下一次 `EffectSound.Start.Before` 把 `clip` 换掉或清空。

## Buff 音效

Buff 表也有 `Effects`、`SoundEffects`、`Action` 等字段。`BuffItem` 初始化时会按 buff 类型播放本体默认的增益/负面音效，并且如果 `SoundEffects` 不为空，也会调用：

```text
AudioManager.PlayEffect(config.dataConfig.data["SoundEffects"])
```

这条路径是直接通过字符串加载 `Sounds/...` 资源，不经过 `EffectSound`。因此它和出牌动作音效不是同一种替换点。如果未来要统一替换 buff 音效，需要额外 Hook `BuffItem.Init` 或 `AudioManager.PlayEffect(string)`，并用更严格的上下文避免误伤。

## 为什么不全局替换 AudioManager.PlayEffect

`AudioManager.PlayEffect` 被非常多系统共用，包括：

- UI 按钮
- 抽牌
- 回合切换
- 卡牌滑动
- 获得增益/负面
- 死亡
- 金币
- 地图操作

全局 Hook 这个入口虽然最直接，但上下文最少，误伤风险最大。共享层当前的选择是：

- 角色状态语音：Hook `StatusManager.PlayVocal`。
- 卡牌动作音效：Hook `FightUI.CallActionAnimation` + `EffectSound.Start`。
- 低血量旁白压制：Hook `NarrationManager.Play`。

这种设计更接近“在语义边界拦截”，而不是“在最终播放口拦截”。

## 对 Mod 开发的启发

如果要新增角色技能语音，有两种可靠做法：

- 技能逻辑强绑定：在 C# 技能脚本中主动调用 `AudioApi`，发送自定义 `Kind`。
- 替换本体语音状态：注册 `kind: "VocalState"`、`vocalState: "Skill"` 的 provider，并用 `careerIds` / `roleIds` / `cardIds` 限定范围。

如果要新增出牌动作音效替换，优先使用：

```text
kind: CardUse
bus: Effect
policy: ReplaceOriginal
match.cardIds: [...]
```

不要优先 Hook `AudioManager.PlayEffect(string)`，除非要处理的是本体字符串音效路径，并且有足够上下文做过滤。

## 当前 SunExp 对照

SunExp 的乌娜主动技能卡在 `SunExp/Data/Card/wuna.csv` 中使用：

```text
Action = Skill
Effects = 空
UseScript = CS.SunExp.Dll.Scripting.WunaScripts.Use(...)
```

因此本体会把它当作 `Skill` 动作，并用角色默认技能特效兜底；语音则由 `WunaScripts` 主动调用共享音频系统播放。

这解释了当前实现的分工：

- 游戏本体负责动作队列、角色动画、默认特效。
- SunExp 技能脚本负责技能效果和精确语音触发。
- 共享音频层负责资源加载、匹配、播放、压制和联机同步。
