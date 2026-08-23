# AuraToolsExp 原生战斗回放 v11 终局架构

状态：当前唯一生产设计。v10 合成场景播放器已退役，不允许与 v11 双轨发布。

## 1. 产品契约

1. 新录制只产生 `Replay Document v11`，公开协议和 `.aurareplay` 包版本均为 11/11。
2. 结构化记录是权威数据，MP4 是由同一原生表现会话生成的冻结媒体。
3. 回放完整复用游戏战斗背景、角色、联机队友、敌人、伙伴、FightUI、手牌、意图、Buff、状态、动作、特效和结果提示。
4. 回放只投影已记录事实；不得执行卡牌/Buff/职业/遗物脚本、AI、随机数、行动队列、奖励、存档写入、Command、RPC 或本地网络 Host。
5. 原生一致性优先于脱离依赖运行。游戏、Aura 和内容 MOD 的程序集指纹不一致时拒绝结构化播放；已验证 MP4 仍可观看。
6. 缺少场景、实体、动作表现、必需 PCM、资源附件或哈希时，记录不能进入 `Ready`，也没有降级播放器。
7. v10 合成回放单向迁移为 `SummaryOnly`：保留对局摘要、分析和已验证 MP4，删除旧文档、时间线及附件引用。

## 2. 唯一数据流

```text
真实权威战斗
  -> MatchReplayHookAdapter
  -> MatchReplayRecorder
  -> Replay Document v11 + checkpoints + PCM/assets
  -> ReplayDocumentValidatorV11
  -> MatchRecordDatabase (document_version = 11)

Replay Document v11
  -> ReplayNativeDocumentAdapter
  -> MatchReplayPlayer
  -> MatchReplayNativeViewRuntime
  -> MatchReplayFightSandboxInitializer
  -> 原生背景 / FightUI / Player / OtherPlayer / Enemy / Partner
       -> 交互回放
       -> ReplayNativeRenderSurface -> 受控 FFmpeg -> MP4
```

`Replay.Core` 保持纯 .NET，负责模型、规范化 JSON、哈希、检查点和状态投影。
`Recording` 是唯一读取真实战斗对象的层。`Playback` 只构造表现对象并写入记录的
after-value。`GameApi/MatchReplayNativePresentationApi` 是原生动作和特效调用的唯一
适配边界。

## 3. Replay Document v11

### 3.1 文档头

`ReplayDocumentHeaderV11` 保存：

- 文档/最低读取版本 11；
- record、adventure、session、level 和结果身份；
- 游戏、工具、渲染版本；
- `aura-replay-native.v1` 渲染 profile；
- 相关程序集名称、版本和 MVID 生成的 `runtimeFingerprint`；
- `native-battle-view.v1`、`exact-dependency-manifest.v1` 等必要能力；
- 初始/最终状态、事件链、内容清单和整份文档 SHA-256。

### 3.2 原生战斗上下文

`ReplayNativeBattleContextV11` 保存：

- 原生背景场景名；
- 地图模式、层级和录制时游标元数据；
- role queue、temporary roles 和完整 RoleTable 快照；
- 敌方强度与生命系数；
- 每个玩家实例对应的职业 ID 和 owner-qualified 皮肤 ID。

皮肤通过 AuraSkin 的非持久化 scoped selection 应用。作用域拥有 owner 和释放句柄，
不会修改玩家当前皮肤配置。

### 3.3 逻辑状态

状态包含：

- 玩家、远端玩家、敌人和伙伴的实例 ID、owner-qualified 内容 ID、类型、阵营、槽位、生命、防御、资源、状态和 Buff；
- 卡牌实例 ID、内容 ID、区域、顺序、显示费用和表现白名单值；
- 敌方意图、原生槽位、目标、动作和效果；
- 玩家能量、牌堆计数和当前回合。

卡牌状态额外冻结有效主题、卡框、动态效果和参数。播放时通过
`AuraCardPresentationRuntime` 和 CardVisual v4 的 `frame/native-frame-v1` 渲染，
不会读取后来修改的本地映射。

### 3.4 时间线

事件集合为：

- `TurnChanged`
- `ActionStarted`
- `ActionCompleted`
- `StateChanged`
- `BattleCompleted`

每个事件包含严格递增 sequence、整数时间、action/cause 身份、after-value delta、
分析语义、表现提示、PCM 音频提示、状态哈希和事件链哈希。

每个 `ActionCompleted` 必须携带 `ReplayNativeActionPresentationV11`：行动者动画、
效果名、效果延迟、表现持续时间，以及每个目标的 Hit/Defend/Idle 等状态。缺失时
最终化失败，而不是猜测或降级。

### 3.5 检查点与定位

初始、回合边界、周期和最终检查点保存完整逻辑状态及事件链哈希。定位流程为：

1. 取消回放拥有的 Tween、动画队列、飞牌和瞬态结果；
2. 从最近检查点恢复；
3. 静默应用后续 delta；
4. 重新投影角色、敌人、手牌、Buff、意图和 HUD；
5. 从目标动作继续原生表现。

相同目标位置必须得到相同状态哈希。

## 4. 录制边界

- `FightManager.Init` 前保存背景、角色队列、敌方系数与 RoleTable。
- `AuraBattleLifecycleRouter` 建立战斗基线、回合边界和结果。
- `AuraCardLifecycleRouter` 记录普通卡与攻击卡动作。
- `SkillItem.TrueUse` 记录技能动作。
- `OtherObj.DoOneAction` 记录逐个敌方意图。
- `AuraRemoteCombatActionRouter` 记录远端公开行动和随后到达的权威状态。
- `FightUI.CallActionAnimation` 前只读捕获原生动作输入。
- AudioManager 和 AudioArbiter 的最终解析结果冻结为内容寻址 PCM。

录制器不执行任何额外战斗命令。纹理读取走 RGBA RenderTexture；音频按小块在
共享帧调度器读取，再在后台生成 WAV，避免在动作 Hook 中复制整段音频。

## 5. 原生表现会话

回放只允许从无战斗、无 Mirror Server/Client 的菜单状态启动。

`MatchReplayNativeViewRuntime`：

- 保存并替换静态 FightManager，仅创建 `IsFake` 的回放拥有实例；
- 保存并在退出时恢复 FightCardManager 各牌区；
- 不创建 NetworkIdentity、不启动 Host、不绑定玩家网络身份；
- 退出时销毁回放角色、敌人、伙伴、卡牌、FightUI 和管理器，并恢复原静态实例。

`MatchReplayFightSandboxInitializer`：

- 直接显示并初始化 FightUI；
- 按录制 RoleTable/temporary roles 创建 FightPlayer 与 OtherPlayer；
- 按 v11 实体目录创建 Enemy 和 Partner，不读取当前 Level.EnemyIds 推断阵容；
- 在 `FightType.Init` 中构造对象，禁止敌方 SetAction/AI；
- 不调用 `FightInit.Init`、`FightInit.RpcLoadRoles`、职业/遗物/祝福脚本或回合单元。

动作使用原生 `FightUI.AnimationData`、`DOActionAnimation` 和 `IEffectManager`，结果
状态由记录 delta 在命中时点投影。任一必需行动者、目标、意图槽或效果无法构造时
立即停止回放。

战斗结果使用原生 `CaptionUI` 显示记录结果，不调用 Fight_Win/Loss/Escape 逻辑。

## 6. 音频与 MP4

### 6.1 音频

每条音频提示必须引用冻结的 PCM WAV 附件。NativeResourceId 和 provider 信息只作
诊断，不是播放 fallback。附件缺失或解码失败时，交互回放和有声导出均失败。

### 6.2 固定帧导出

1. 通过与交互回放相同的 `MatchReplayPlayer` 启动原生视图；
2. 设置 `Time.captureFramerate`，按输出 FPS 推进外部回放时钟；
3. `ReplayNativeRenderSurface` 把原生相机和根 Canvas 临时绑定到 RenderTexture；
4. 隐藏回放/导出控制条，按配置保留或隐藏战斗 HUD；
5. RGB24 帧进入容量固定的内存管道；
6. PCM 附件按样本时间离线混合；
7. 工具自带且哈希验证的 FFmpeg 生成 MP4；
8. ffprobe 和完整解码验证通过后原子提交。

不允许 ScreenCapture、AudioListener 录音、JPEG spool、AVI、PATH FFmpeg 或网络下载。

## 7. 数据库和迁移

`replay_documents.document_version` 使用硬约束 `CHECK(document_version=11)`。

启动时若发现 v10 表：

1. 统计旧文档和 chunk 字节；
2. 将对应 `battle_records` 改为协议 11 的 `SummaryOnly`；
3. 保留统计、分析和 `replay_media` 中已验证 MP4；
4. 删除旧 export job、文档、时间线和附件引用；
5. 重建 v11 表；
6. 写入 `replay-v10-to-v11-native-cutover` 迁移账本。

v8/v9 及更早 chunks 仍只允许进入授权清理报告，不进入播放器。

## 8. 完成和验收

- 协议清单、源码、数据库、包、设置 UI、测试和文档只声明 11/11。
- 搜索证明没有 `ReplaySceneRuntime`、`ReplayTimelineController`、`StartLocalHost`、
  `RpcLoadRoles`、脚本/AI/RPC 执行或 `native-or-silence`。
- 行为测试覆盖状态哈希、检查点、PCM、包、SQLite v10->v11 切换和 scoped skin。
- 游戏内对比真实战斗与回放：背景、角色/皮肤、远端玩家、敌人/伙伴、动作、
  受击、意图、手牌顺序/费用/卡面效果、Buff、生命、防御、能量和结果提示一致。
- 720p30/1080p30、有无 HUD、有无音频均生成并完整验证 MP4。
- 播放/导出退出后，RoleTable、FightManager、FightCardManager、背景、House、输入、
  Canvas、Tween 和临时作用域全部恢复。
