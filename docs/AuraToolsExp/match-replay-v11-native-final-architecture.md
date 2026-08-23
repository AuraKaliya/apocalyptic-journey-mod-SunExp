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
8. `OutcomeEntering` 后禁止新的战斗表现生产；`BattleFinalized` 是所有 `BattleEnded` 清理完成后的唯一终局封存屏障。
9. 回放启动是准备/提交事务：准备失败时保留当前对局记录页并原位显示错误；只有原生视图准备成功才销毁来源 UI。交互回放结束、主动退出或运行失败都返回同一对局记录页并恢复其逻辑状态。
10. 回放时间轴的 `t=0` 固定为 `BattleMaterialized`：此时玩家、队友、敌人和伙伴已成为可读取的权威实体。此前只捕获原生启动上下文，不产生逻辑状态、动作或音效事件。

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
`AuraCardPresentationRuntime` 和 CardVisual v7 的
`aura.card-visual.material-v5 + frame/native-frame-v4` 渲染，不会读取后来修改的
本地映射。战斗卡只接受同一候选同时提供的精确 visual root、`IDataConfig` 和
`ICard`，不会把相邻池化卡牌的 root 与当前卡牌数据拼接。

初态与 delta 中的每张卡都显式保存 `Tag`、`Rarity` 和 `Icon`。`Tag` 允许为空字符串，
但字段本身不得缺失；回放恢复只组装表现字典，不调用 `FightCardManager.CardTagCheck`，
也不为只读回放重建玩法索引。

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

- `FightManager.Init` 前只保存背景、角色队列、敌方系数与 RoleTable，并建立尚未开放时间线的录制会话。
- `AuraBattleLifecycleRouter.BattleMaterialized` 是唯一基线提交点；它验证完整的本地玩家与 owner-qualified 敌方实体、重置回放时钟，并把当前实际 BGM 作为 `t=0` 音频。
- `BattleMaterialized` 前的装载、抽牌和 UI 音效不属于回放时间线；其最终状态已经进入权威基线，不以缺少画面的音频前奏重复播放。
- `PlayerRoundReady` 只记录相对于已提交基线的首个回合 delta，不得再创建或替换基线。
- `AuraCardLifecycleRouter` 记录普通卡与攻击卡动作。
- `SkillItem.TrueUse` 记录技能动作。
- `OtherObj.DoOneAction` 记录逐个敌方意图。
- `AuraRemoteCombatActionRouter` 记录远端公开行动和随后到达的权威状态。
- `FightUI.CallActionAnimation` 前只读捕获原生动作输入。
- `OutcomeEntering` 原子关闭抽牌、卡牌物化等瞬态生产者；`BattleSettling` 完成当前动作，`BattleEnded` 清空原生/工具队列和手牌，`BattleFinalized` 才捕获空手牌终局并写入 `BattleCompleted`。
- AudioManager 的字符串重载只用于关联身份，不创建 cue；只有实际进入播放重载的 `AudioClip` 和 AudioArbiter 的最终解析 Clip 才冻结为内容寻址 PCM。

录制器不执行任何额外战斗命令。纹理读取走 RGBA RenderTexture；音频按小块在
共享帧调度器读取，再在后台生成 WAV，避免在动作 Hook 中复制整段音频。

## 5. 原生表现会话

回放只允许从无战斗、无 Mirror Server/Client 的菜单状态启动。

`MatchReplayNativeViewRuntime`：

- 保存并替换静态 FightManager，仅创建 `IsFake` 的回放拥有实例；
- 保存并在退出时恢复 FightCardManager 各牌区；
- 不创建 NetworkIdentity、不启动 Host、不绑定玩家网络身份；
- 每个角色、敌人和伙伴实例化后立即登记到回放对象所有权表，不依赖可能尚未完成的 `FightManager.statuses` 反推所有权；
- 退出时销毁回放角色、敌人、伙伴、卡牌、FightUI 和管理器，并在下一 Unity 帧验证销毁终态后恢复原静态实例。

`MatchReplayFightSandboxInitializer`：

- 创建并初始化隐藏的 FightUI，来源 UI 提交后才激活；
- 按录制 RoleTable/temporary roles 创建 FightPlayer 与 OtherPlayer；
- 使用具体 `StatusManager` 先登记 `FightManager.statuses`，再读取通过该字典解析的 `FightPlayer.Status`；
- 按 v11 实体目录创建 Enemy 和 Partner，不读取当前 Level.EnemyIds 推断阵容；
- 在 `FightType.Init` 中构造对象，禁止敌方 SetAction/AI；
- 不调用 `FightInit.Init`、`FightInit.RpcLoadRoles`、`ResetWaitCount` 等 Command、职业/遗物/祝福脚本或回合单元。

### 5.1 启动、回滚与返回

`MatchReplayLifecycleState` 是唯一会话状态机：`Idle -> Preparing -> Prepared -> Active -> Exiting -> Idle`。

1. `Preparing` 在当前资料库 UI 仍存活时验证文档、安装临时 RoleTable、创建隐藏 FightUI，并构造全部原生表现对象。
2. 任何准备错误都销毁已登记对象、恢复环境并把错误写回当前资料库页面；来源 `SettingUI` 不关闭。
3. 进入 `Prepared` 后才提交来源 UI，统一由 `MatchReplayUiLifecycle` 清理设置实例和 AuraTools Overlay；调用方不得再各自关闭一遍。
4. 提交完成后激活 FightUI 和交互控制条，进入 `Active`。
5. 正常完成、主动退出、运行错误和用户发起的视频导出结束都进入同一 `Exiting`；回放 UI、对象、静态实例、牌区、背景和临时作用域全部清理后才离开该状态。
6. `MatchReplayReturnCoordinator` 只保存搜索、筛选、分页、选择、编辑项和滚动锚点等纯逻辑状态，不保存已销毁的 Transform/Canvas 引用。
7. 返回时先确认旧 `SettingUI` 已销毁，再同步建立原生“预热且隐藏”的缓存状态，重新打开 AuraTools 面板和对局资料库。旧实例的延迟 Close/Hide/OnDestroy 不能操作新实例。

无人值守的启动恢复导出没有用户来源页面，因此完成后不制造资料库返回目标；它仍复用同一准备、激活和退出状态机。

动作使用原生 `FightUI.AnimationData`、`DOActionAnimation` 和 `IEffectManager`，结果
状态由记录 delta 在命中时点投影。任一必需行动者、目标、意图槽或效果无法构造时
立即停止回放。

战斗结果使用原生 `CaptionUI` 显示记录结果，不调用 Fight_Win/Loss/Escape 逻辑。

## 6. 音频与 MP4

### 6.1 音频

每条音频提示必须引用冻结的 PCM WAV 附件。NativeResourceId 和 provider 信息只作
诊断，不是播放 fallback。字符串路径解析不到 Clip 时不制造“已播放”事件；实际
播放 Clip 若无法加载或读取，则使用 `clip-load-*`、`get-data-*`、调度/最终化错误码
拒绝 `Ready`。被拒绝的文档、时间线和已成功附件以 `Rejected` 诊断草稿随摘要受同一
自动保留上限管理，不能进入播放器或导出器。

`ReplayPcm16WaveContractV11` 是唯一 WAV 写入、解析和结构校验入口。规范附件固定为
RIFF/WAVE、PCM format 1、16-bit、1/2 声道，且 byteRate、blockAlign、data 长度和
ReplayAttachment 元数据必须一致。录制最终化、包导入、交互播放与离线混音不得维护
各自的 WAV 解析器；即使附件 SHA 和长度正确，结构不合格也不能进入 `Ready`。

非流式 Clip 由主线程协作切片调用 `AudioClip.GetData`。`AudioClipLoadType.Streaming`
禁止进入该 API；它通过 `GameApi/AudioClipPcmReadApi` 创建 Unity 原生
`AudioSampleProvider`，从片段起点消费解码后的交错样本并冻结为相同的 PCM WAV。
读取器直接持有 Unity 返回的 `ConsumeSampleFramesNativeFunction` 委托；不得把其函数
指针重新封装成另一个托管委托类型，因为 Mono 会保留原委托身份并拒绝跨类型转换。
该路径读取 AudioClip 自身的解码器，不录制 AudioListener、混音器输出或系统声音。

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

数据库 v5 还执行一次 `replay-v11-empty-bootstrap-to-materialized-baseline` 切换：旧 v11
若初态为空、且首个完整玩家回合之前只有无状态音频事件，则以该回合状态重建基线，
保留实际 BGM，移除无画面的前奏音效与无引用附件，并重算事件、检查点和全部哈希。
无法证明此前没有动作或状态语义的文档改为 `SummaryOnly + Rejected`；损坏文档改为
`Corrupt`，播放器不保留从首个 delta 临时补实体的兼容路径。

数据库 v6 执行一次 `replay-v11-card-presentation-empty-tag` 切换：对哈希有效且唯一缺失
`Tag` 字段的旧 v11 卡牌快照补入显式空字符串，重新最终化、分块并计算文档哈希；无法
满足其余表现字段或哈希约束的记录退出 `Ready`。`.aurareplay` 导入先验证包内原始
`DocumentHash`，随后只允许执行同一项缺失 `Tag` 的有界内存迁移，不接受其它校验错误。

数据库 v7 执行一次 `replay-v11-pcm16-wave-header` 切换：对原始文件 SHA、RIFF/data
长度、PCM format、声道、采样率、byteRate、blockAlign 和数据库元数据全部一致，且唯一
缺少 16-bit 字段的旧 WAV 生成规范新文件；随后重写所有 Ready/Rejected v11 文档中的
附件和 cue 身份，重新计算事件链、检查点、分块与文档哈希。无引用旧文件移入可恢复
隔离区；其它损坏音频令对应 Ready 文档变为 `Corrupt`。旧 `.aurareplay` 包在原始
DocumentHash/entry 哈希通过后执行同一项有界迁移，不保留播放器猜测路径。

## 8. 完成和验收

- 协议清单、源码、数据库、包、设置 UI、测试和文档只声明 11/11。
- 搜索证明没有 `ReplaySceneRuntime`、`ReplayTimelineController`、`StartLocalHost`、
  `RpcLoadRoles`、脚本/AI/RPC 执行或 `native-or-silence`。
- 行为测试覆盖状态哈希、检查点、`BattleMaterialized` 基线门、空基线 `Ready` 拒绝、数据库 v5 确定性重建、数据库 v6 显式空 `Tag` 迁移、数据库 v7 PCM 内容寻址迁移、实际 Clip 调用关联、终局封存屏障、致命伤害后抽牌拒绝、原生抽牌队列清理、Rejected 诊断草稿、PCM、包、SQLite v10->v11 切换和 scoped skin。
- 游戏内对比真实战斗与回放：背景、角色/皮肤、远端玩家、敌人/伙伴、动作、
  受击、意图、手牌顺序/费用/卡面效果、Buff、生命、防御、能量和结果提示一致。
- 720p30/1080p30、有无 HUD、有无音频均生成并完整验证 MP4。
- 播放/用户发起的导出退出后，RoleTable、FightManager、FightPlayer、FightCardManager、背景、House、输入、Canvas、Tween 和临时作用域全部恢复，并自动回到保留页面状态的对局资料库。
- 行为测试覆盖准备失败不提交来源 UI、提交后所有退出原因都返回资料库、无人值守导出不创建返回目标、资料库逻辑状态深拷贝，以及具体 Status 先注册再通过 `FightPlayer.Status` 解析的顺序。
