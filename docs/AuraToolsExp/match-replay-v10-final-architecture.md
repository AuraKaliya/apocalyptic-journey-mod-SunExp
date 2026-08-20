# AuraToolsExp 回放 v10 终局架构

状态：最终设计基线。实现可以拆分任务，但发布只能一次性切换，不允许同时
发布旧播放器和 v10 播放器。

## 1. 最终产品契约

1. 新录制只产生 `Replay Document v10`。
2. 发布运行时只播放 v10，不播放、降级播放或模拟执行 v8/v9。
3. v8/v9 只能进入隔离的单向迁移器，不能进入播放器。
4. 回放使用 AuraToolsExp 自有的确定性 2D 表现语言，不复刻原生
   `FightUI`、模型战斗或原生脚本动画。
5. 逻辑状态、事件顺序、表现提示和音频时间必须确定；不同播放器构建之间
   不承诺逐像素相同。MP4 是逐像素冻结的发布结果。
6. 所有被回放引用的内容展示信息和静态资源必须随文档保存。缺失必需附件的
   录制不能进入 `Ready`。
7. 交互播放与视频导出使用同一个投影器、表现树和资源解析器，只替换时钟和
   输出表面。
8. AuraToolsExp 管理的视频统一为经过完整验证的 MP4。导入的其他格式必须
   转码后才能入库，不能作为第二种持久格式保留。
9. 战斗摘要、统计和冒险关联独立于可播放回放。删除或无法迁移回放不得默认
   删除统计。

公开版本统一为：

- `documentVersion = 10`
- `minimumReadableDocumentVersion = 10`
- `.aurareplay` 的 `packageVersion = 10`
- `records.match-replay` 发布清单为 `10 / 10`

SQLite `PRAGMA user_version` 仍表示数据库结构版本，按现有 v3 递增，不与公开
回放协议强行共用数字。数据库行必须另外保存并约束 `document_version = 10`。

## 2. 所有权与依赖边界

```text
真实战斗
  -> ReplayCaptureAdapter          只观察真实权威结果
  -> ReplayDocumentBuilderV10      生成自包含文档
  -> ReplayDocumentValidatorV10    完整性、哈希和可投影性验证
  -> ReplayRepository              保存文档、附件和摘要

ReplayDocumentV10
  -> ReplayProjectionEngine        纯 .NET、确定性状态
  -> ReplayPresentationDirector    确定性表现状态
  -> ReplaySceneRuntime            AuraToolsExp 自有 Unity 场景
       -> InteractiveReplayClock   交互播放
       -> FixedStepReplayClock     离线视频
```

边界规则：

- `Replay.Core`：只包含 v10 模型、规范化序列化、哈希、验证和投影；不得引用
  Unity、Witch、Mirror、AuraTools UI 或文件系统。
- `Replay.Capture`：唯一允许读取 `FightManager`、`RoleTable`、卡牌和敌人运行态
  的回放层；只能输出 v10 事实和资源快照，不能包含播放逻辑。
- `Replay.Presentation`：可以引用 UnityEngine 和 Aura 的无业务 UI、缓存、对象池
  基础；不得引用 Witch 战斗对象、网络对象或 Terrias 内部实现。
- `Replay.Export`：只消费表现运行时、固定时钟、帧和音频事件，不读取真实战斗。
- `Replay.Storage`：拥有回放文档、附件、媒体、任务和迁移账本。
- `Replay.LegacyMigration`：只读 v8/v9，并只输出 v10 或迁移报告；没有播放入口。

AuraToolsExp 继续是回放语义的唯一所有者。只有已经存在且真正无业务语义的
UI、资源缓存、日志和对象池基础可以复用 Aura shared；本次不为回放私有需求
新增共享领域协议，也不读取 Terrias 私有目录。

## 3. Replay Document v10

### 3.1 文档头

`ReplayDocumentHeaderV10` 至少包含：

- `documentVersion`、`recordId`、`adventureId`、`sessionId`；
- `startedUtc`、`endedUtc`、`result`；
- `gameBuild`、`toolBuild`、`rendererBuild`、`renderProfileId`；
- `timebaseTicksPerSecond`，固定为 `1_000_000`；
- `contentManifestSha256`、`timelineRootSha256`；
- `initialLogicalStateSha256`、`finalLogicalStateSha256`；
- `finalEventChainSha256`、`documentSha256`；
- `requiredFeatures`，v10 中不得以 optional capability 绕过必需事实。

协议哈希使用小写十六进制 SHA-256。哈希目标是规范化后的未压缩逻辑字节，
包级文件哈希则校验实际存储字节；两者不能混用。

### 3.2 内容引用和来源

所有内容使用 owner-qualified 引用：

```text
ownerModId + contentKind + stableContentId
```

不得把当前 CSV 行号、资源绝对路径、Unity instance id 或显示名称当成内容 ID。
来源清单按 owner 保存版本、清单哈希和文件哈希，仅用于溯源和迁移判断；播放
不从已安装 MOD 重新解析这些内容。

### 3.3 实体目录

文档必须在时间线前声明所有初始实体：

- 实体实例 ID、内容引用、队伍、拥有者、顺序和槽位；
- 玩家角色、远端角色、召唤物和敌人的明确类型；
- 初始生命、上限、防御、资源、状态和 Buff；
- 卡牌实例 ID、内容引用、初始区域、区域内顺序和动态展示值；
- 敌方意图实例 ID、所属实体、槽位、目标和初始展示；
- 关卡 ID、背景附件、布局配置和战斗结果展示配置。

实例 ID 只在单份文档内唯一。状态投影、时间线、音频和表现提示只能引用已经
声明或通过明确 `EntityCreated` / `CardCreated` 事件创建的实例。

### 3.4 表现快照

每种被引用内容都保存结构化 `ReplayDisplaySnapshotV10`：

- 本地化后的名称、副标题、描述和规则文本；
- 卡牌费用、标签、稀有度、颜色和布局所需数值；
- 图标、卡图、头像、立绘和背景的附件引用；
- Buff、意图、状态和数值提示的最终显示文本；
- 通用表现类别，例如 attack、skill、hit、heal、block、death。

不得保存可执行脚本、反序列化游戏类型、完整 `RoleTableJson`、任意 CSV 字典或
原生组件类型名作为播放契约。录制器必须把它们转换成 v10 白名单字段。

### 3.5 附件

附件使用内容寻址：

```text
sha256 + mediaType + byteLength + dimensions/audioFormat + usage
```

- 图像规范化为受支持的 PNG/JPEG，保存像素尺寸和色彩空间。
- 离线音频规范化为受支持的无损音频或 PCM，保存采样率、声道和样本数。
- 禁止脚本、DLL、EXE、任意 Unity AssetBundle、着色器和外部 URL。
- 文档不得保存绝对路径；包内路径由附件哈希推导。
- 导入前限制附件数量、单文件大小、总解压大小、像素数、声道数和持续时间。
- 必需附件缺失、哈希错误或无法解码时，整份回放不进入 `Ready`。

### 3.6 权威时间线

时间线事件都包含：

- 严格递增的 `sequence`；
- 整数 `timeTicks`；
- `eventId`、`actionId`、`causeEventId`；
- 枚举事件类型和类型专属负载；
- `stateHashAfter`，仅状态变化事件必填；
- `eventChainHashAfter`。

v10 使用封闭的事件集合：

- `TurnChanged`、`ActionStarted`、`ActionCompleted`、`BattleCompleted`；
- `EntityCreated`、`EntityRemoved`、`EntityStateSet`；
- `DamageApplied`、`HealingApplied`、`DefenseSet`、`ResourceSet`；
- `BuffUpserted`、`BuffRemoved`；
- `CardCreated`、`CardMoved`、`CardUpdated`、`CardRemoved`；
- `IntentUpserted`、`IntentRemoved`；
- `PresentationCue`、`AudioCue`。

状态事件优先记录权威的 after 值，并同时记录用于分析的变化量。投影器不执行
伤害公式、治疗公式、Buff 脚本、卡牌脚本、AI 或随机数。

逻辑状态不得依赖文化区域字符串或普通浮点哈希。整数状态直接编码；确实来自
游戏 float 的事实使用明确的 IEEE-754 bit pattern 或协议定义的定点数。

### 3.7 检查点

文档包含初始检查点、定期检查点和最终检查点。每个检查点保存：

- 已应用的最后事件序号和时间；
- 完整逻辑状态；
- 逻辑状态 SHA-256；
- 对应事件链 SHA-256。

写入器默认每 128 个状态事件生成检查点，并在回合边界补充检查点。该间隔写入
文档头供诊断，但读取器不依赖固定间隔。

## 4. 规范化与确定性验证

v10 使用显式属性顺序的规范化 UTF-8 JSON；字典不得直接进入哈希域，必须转换
成按 ordinal key 排序的数组。时间、数字、空值和字符串转义规则固定。压缩层
使用 gzip 只影响存储，不影响逻辑哈希。

最终化必须从初始状态重新运行纯投影器并验证：

1. 文档、事件、实例和附件引用完整；
2. sequence、timeTicks 和因果引用合法；
3. 每个状态事件的 `stateHashAfter` 正确；
4. 每个检查点可由前一检查点和事件重建；
5. 最终状态、事件链和内容清单哈希一致；
6. 不存在外部 CSV、MOD 路径或运行时对象依赖；
7. 所有必需表现和音频附件可解码。

任一验证失败时只保存战斗摘要、统计和录制诊断，不保存伪 `Ready` 回放，也不
提供“降级继续播放”。

## 5. 专用投影与表现运行时

### 5.1 纯投影器

`ReplayProjectionEngine` 的输入只有已验证的 v10 文档。它提供：

- `Reset(initialCheckpoint)`；
- `Apply(event)`；
- `Restore(checkpoint)`；
- `ProjectTo(sequence/timeTicks)`；
- 当前只读 `ReplayLogicalState`；
- 状态哈希和事件链验证结果。

跳转时从最近检查点恢复，以无瞬态表现模式应用事件到目标位置，再重建当前可见
意图、Buff、手牌和 HUD。瞬态数字、受击和卡牌飞行动画不会在跳转后残留。

### 5.2 表现状态

`ReplayPresentationDirector` 把逻辑状态和已录制的表现提示转换为稳定的
`ReplayViewState`。它不查询内容配置，也不根据伤害结果猜动作来源。

`ReplaySceneRuntime` 创建并完全拥有独立 Unity Scene、Camera、Canvas、
EventSystem 和对象根。核心组件为：

- `ReplayActorView`
- `ReplayCardView`
- `ReplayIntentView`
- `ReplayHud`
- `ReplayTimelineController`
- `ReplayAudioBus`
- `ReplayRenderSurface`

角色和敌人使用工具自有的 2D 站位与通用动作语言。内容差异由附件中的头像、
立绘、图标、卡图和文本表达，不调用原生 Enemy、CardItem、SkillItem 或 FightUI。

交互控制必须包含播放/暂停、速度、上一/下一动作、上一/下一回合、时间轴跳转、
静音和退出。退出只销毁运行时自己拥有的 Scene 和资源租约，不修改任何游戏
战斗、网络、ChatUI、RoleTable 或 TempData 单例。

### 5.3 时钟

- `InteractiveReplayClock` 根据真实时间推进，可以丢弃渲染帧，但不能跳过逻辑
  事件。
- `FixedStepReplayClock` 按输出帧率从帧序号计算文档时间，不读取
  `Time.deltaTime`、DSP 实时时钟或当前游戏帧率。

两种时钟驱动同一个投影器和表现树。

## 6. 音频与视频导出

### 6.1 音频事件

`AudioCue` 至少保存：

- 音频附件哈希；
- 开始样本、源偏移、持续样本；
- gain、pan、播放速率；
- 循环区间、淡入和淡出；
- bus 类型和事件来源。

随机音效变体必须在录制时解析为具体附件。BGM 的开始位置、停止、循环和淡化
同样记录。离线混音器以固定采样率生成确定长度的 PCM，不使用 AudioListener。

### 6.2 帧管道

1. 创建专用 `RenderTexture` 和固定输出 profile。
2. 固定时钟推进到目标帧时间。
3. 同一 `ReplaySceneRuntime` 渲染到纹理。
4. 通过 GPU readback 或 RenderTexture readback 取得 RGB 帧。
5. 帧进入容量固定的内存管道；管道满时暂停离线推进，不丢帧。
6. 原始帧直接进入受控编码器，不生成 JPEG spool。
7. 固定 PCM 音频作为第二输入。

第一版固定支持经过测试的 720p30 和 1080p30 profile。新增 profile 必须作为
版本化发布能力进入验证，不开放任意编码参数。

### 6.3 编码器

- 只使用 AuraToolsExp 随工具发布的 FFmpeg/ffprobe；不读取 PATH 和用户路径。
- 依赖清单保存平台、版本、构建参数、许可证文件和 SHA-256。
- 启动任务前校验二进制哈希，不匹配则拒绝导出。
- 产品格式固定为 MP4，视频/音频 codec 和参数由发布 profile 固定。
- FFmpeg 分发必须通过 LGPL 兼容性和所选编码器的许可证发布门禁。
- 编码只写同一目标目录中的 `.partial` 文件，不覆盖现有 Ready 媒体。

### 6.4 视频验证

编码成功不等于任务成功。验证步骤必须同时执行：

- ffprobe 检查容器、视频流、音频流、codec、分辨率、时间基和时长；
- 解码器完整读取所有帧和全部音频，不只读取文件头；
- 核对帧数、FPS、预计时长、音频样本数和允许误差；
- 计算最终文件 SHA-256；
- 拒绝空视频、截断视频、多余流和 profile 不匹配。

## 7. 数据库模型

数据库继续使用当前 MatchRecords SQLite 文件，但拆开摘要、回放、附件、媒体和
任务所有权。

### `battle_records`

只保存记录 ID、冒险/会话关联、关卡、结果、时间、收藏、标签、备注和统计摘要。
它可以在没有可播放回放时存在。迁移时重建旧表，移除旧 `initial_payload` 和把
回放状态塞进摘要行的做法。

### `replay_documents`

一行对应一份可播放 v10 文档，保存：

- `record_id`、`document_version`、`document_state`；
- 文档头负载和文档 SHA-256；
- 初始/最终状态哈希、事件链哈希；
- 事件、检查点、附件计数和字节数；
- 验证器/渲染 profile 版本。

`document_state` 只允许 `Finalizing`、`Ready`、`Corrupt`。播放器只读取 Ready。

### `replay_chunks`

只保存 v10 timeline chunks，包含严格连续索引、事件范围、时间范围、压缩负载和
存储 SHA-256。旧 chunks 不允许和 v10 混存。

### `replay_assets` 与 `replay_asset_refs`

附件表按 SHA-256 去重并保存规范化媒体信息和相对路径；引用表连接 record 和
用途。清理使用引用关系和 mark-and-sweep，不依赖目录名猜测。

### `match_analysis`

保留独立分析协议。分析可以来自 v10，也可以来自不可迁移旧录像的事件摘要。

### `replay_media`

只登记已经验证并提交的 MP4，保存 profile、文件相对路径、时长、帧数、音频
样本数、字节数和 SHA-256。缺失或哈希错误时标记 `Corrupt`，不能静默删除。

### `replay_export_jobs`

保存全部任务历史，而不是一个 `current.json`：

- job、record、profile、状态和状态 revision；
- 创建/更新时间、尝试次数、取消标记；
- staging 路径、目标路径、预期/验证后哈希；
- 输出元数据、错误码、错误消息和恢复记录。

### `replay_migrations`

保存旧记录扫描分类、证据、目标 record、报告路径、处理状态和清理授权批次。

所有子表使用真实外键和级联策略。数据库路径仍由 AuraShared owner-system 路径
提供，但回放表和业务语义归 AuraToolsExp。

## 8. 持久化任务状态机

```text
Planned -> Rendering -> Encoding -> Validating -> Committing -> Ready
              |            |            |
              +------------+------------+-> Failed / Cancelled
```

状态转换使用数据库事务和 revision compare-and-swap。只有一个 worker 可以持有
某个活跃任务；可以排队多个 Planned 任务，但 GPU 渲染默认串行。

提交协议固定为：

1. 在最终媒体目录创建唯一 `.partial` 输出。
2. 编码结束并关闭文件句柄。
3. 完整验证并计算哈希。
4. 数据库写入 `Committing`、目标路径、临时路径、哈希和媒体元数据。
5. 在同一目录原子移动 `.partial` 到最终 `.mp4`。
6. 一个数据库事务 upsert `replay_media` 并把任务标记为 `Ready`。
7. 最终文件和数据库行均以 job id 实现幂等。

进入 `Committing` 后忽略取消请求并完成或恢复提交，避免产生未知归属的最终文件。

启动恢复规则：

| 状态 | 恢复动作 |
|---|---|
| Planned | 重新进入队列 |
| Rendering | 删除任务 partial，标记可重试 Failed |
| Encoding | 删除任务 partial，标记可重试 Failed |
| Validating | 文件存在则重新完整验证，否则 Failed |
| Committing | 根据 staging/target 和记录哈希完成原子移动与登记 |
| Ready | 校验文件存在和哈希；不一致则媒体标记 Corrupt |
| Failed/Cancelled | 回收 partial，保留任务日志 |

文件存在但没有 job 或 media 证据时不能猜测所属记录；先移入隔离区并写扫描报告，
在确认的保留期后删除。数据库有媒体记录但文件缺失时标记 Corrupt，并保留审计
信息，不制造空文件。

包导入使用同样的 staging、验证、数据库登记和恢复原则，不直接把附件解压到
Ready 目录。

## 9. v10 回放包

`.aurareplay` v10 使用以下布局：

```text
manifest.json
document.json.gz
timeline/000000.json.gz
timeline/000001.json.gz
checkpoints/000000.json.gz
attachments/<sha256>.<extension>
analysis/summary.json.gz
```

`manifest.json` 明确列出每个 entry 的类型、压缩后大小、解压后大小和 SHA-256，
并保存文档逻辑哈希。导入器只接受清单中声明的规范路径，不接受额外 entry、
重复名称、反斜杠、`..`、绝对路径、符号链接或大小不一致。

导出包在同目录生成临时文件，关闭并重新打开完成全包验证后再原子移动。导入先
完整验证到 staging，再把附件按哈希去重提交，最后在一个数据库事务登记摘要、
文档、chunks、附件引用和分析。

## 10. v8/v9 迁移

### 10.1 原则

现有 `ContentSha256` 不是原始游戏/MOD 内容哈希，现有 `ModFingerprint` 也没有
覆盖 MOD 文件。因此二者只能证明旧回放负载自身一致，不能证明当前内容就是
录制时内容。

迁移必须先运行只读扫描并生成机器可读 JSON 和用户可读报告。扫描不得删除或
改写旧数据。报告至少包含：

- 记录、协议、chunks、检查点和事件完整性；
- 已保存的实体、卡牌、敌人、Buff、意图和表现字段；
- 每个必需资源是否存在可验证的原始字节；
- 可无损转换、仅摘要、损坏和需外部归档四种分类；
- 将保留/新增/删除的数据库行与文件精确列表；
- 对局统计、冒险关联和媒体的独立影响。

### 10.2 判定矩阵

| 旧数据 | 处理结果 |
|---|---|
| v8/v9，完整状态/事件/实体/表现且所有资源有可验证字节 | 转换并重新验证为 v10 Ready |
| v8/v9，缺敌人内容 ID、实例、展示字段或必需附件 | 保留战斗摘要、统计、事件摘要和迁移报告，不生成 v10 回放 |
| v8/v9，另有录制时完整内容归档并能逐资源校验 | 使用该归档转换；不得使用当前安装内容猜测 |
| v7 及更早命令回放 | 不执行命令，只生成可提取的统计/摘要 |
| chunks 缺失、顺序断裂或哈希错误 | 标记损坏并报告，不尝试猜测修复 |
| 已关联且可完整验证的 MP4 | 保留并登记到新媒体表 |
| AVI/MOV/WebM 等旧媒体 | 关联明确时转码和验证为 MP4；否则进入隔离报告 |
| 数据库行存在但媒体缺失 | 标记旧媒体损坏，不影响统计 |
| 文件存在但无数据库/任务证据 | 隔离，等待清理授权，不自动补登记 |

默认保留战斗统计和冒险关联。只有扫描报告给出精确行数和记录 ID 后，才单独
确认是否删除统计。旧录像无法无损转换不构成删除统计的理由。

### 10.3 单向迁移器

旧模型和解析器放在 `Replay.LegacyMigration`，不被运行时播放器引用。它只能：

- 读取旧数据库和 v1 `.aurareplay`；
- 验证旧 chunks；
- 生成 v10 builder 输入或摘要；
- 写迁移账本和报告。

迁移及授权清理完成后，数据库不再包含旧协议行或旧 chunks。发布代码仍可保留
有明确期限的导入迁移入口，但不存在 v8/v9 播放、兼容协商或降级继续能力。

## 11. 旧实现删除与重写清单

### 11.1 整体删除

删除当前 `Features/MatchRecords/Playback/` 下全部旧文件，以新的 `Replay/Core`、
`Replay/Presentation` 和 `Replay/Runtime` 结构替代。特别包括：

- `MatchReplayLocalHostRuntime`
- `MatchReplayRuntimeBootstrap`
- `MatchReplayFightSandboxInitializer`
- `MatchReplayLifecycleRunner`
- `MatchReplayLaunchCoordinator`
- `MatchReplayEnvironmentScope`
- `MatchReplayModeContext`
- `MatchReplayUiLifecycle`
- `MatchReplayManagedUiOwnership`
- `MatchReplayChatUiHookAdapter`
- `MatchReplayChatUiLeaseRuntime`
- `MatchReplayChatUiLifecyclePolicy`
- `MatchReplayExitPolicy`
- `MatchReplayStateCapture`
- `MatchReplayCardStateCapture`
- `MatchReplayPresentationDirector`
- `MatchReplayEnemyIntentPresenter`
- `MatchReplayPassiveBuffPresenter`
- `MatchReplaySkillPresenter`
- `MatchReplayPlayer` 及所有 FightUI 控件适配
- v8/v9 `MatchReplayCompatibility` 和降级继续 UI

删除：

- `GameApi/MatchReplayNativePresentationApi.cs`
- `GameApi/MatchReplayEnemyIntentApi.cs`
- `Media/ReplayWaveCapture.cs`
- `Media/ReplayFrameSpool.cs`
- `Media/MjpegAviWriter.cs`
- 旧 `MatchReplayVideoExporter.cs`
- 旧 `MatchReplayVideoEncoder.cs`
- 旧 `MatchReplayVideoEncodingPolicy.cs`
- `MatchReplayExportJobStore.cs`
- 依赖真实播放环境的旧 export clock/policy
- `AuraToolsExp.Dll.csproj` 中不再被其他功能使用的
  `UnityEngine.ScreenCaptureModule` 引用

### 11.2 重写而非沿用旧契约

- `MatchRecordModels.cs`：拆成战斗摘要和 v10 文档模型。
- `MatchReplayProjectionModels.cs`：保留纯投影思想，改为 v10 封闭事件模型、
  规范化哈希和不可变只读状态。
- `MatchReplayChunker.cs`、`MatchReplayPayload.cs`：改为 v10 规范化负载。
- `MatchReplayRecorder.cs`：只输出 v10 事实，不序列化游戏对象。
- `MatchReplayHookAdapter.cs`：保留录制观察职责，移除所有播放/ChatUI hook。
- `MatchReplayPackageService.cs`：只负责 v10 包；旧包转入迁移器。
- `MatchRecordDatabase.cs`、`MatchRecordsDatabaseMigrator.cs`：实现新表、任务和迁移
  账本。
- `MatchReplayMediaStore.cs`：只接受验证后的 MP4 和内容寻址附件。
- `MatchReplayVideoPlayer.cs`：只播放已验证 MP4，不作为交互回放播放器。
- `MatchReplayMediaSection.cs`、`MatchAnalysisPresenter.cs`、
  `MatchRecordLibraryPresenter.cs`：调用新播放、迁移和任务 API。
- `AuraToolsMatchRecordsRuntime.cs`：只初始化录制、v10 场景、任务恢复和数据库，
  不注册 ChatUI 回放 hook。

### 11.3 配置、测试和文档删除

删除配置属性和 UI：

- `PreferMp4`
- `FfmpegPath`
- AVI fallback/格式偏好
- 已退役的原生表现模式和降级播放开关

保留分辨率 profile、是否包含 ReplayHud、是否包含音频等产品设置，但它们只能
选择受测 profile，不能生成任意编码参数。

删除旧测试：

- 只检查 `RIFF` / `AVI` / MJPEG 文件头的测试；
- v8/v9 可播放、能力协商和降级继续测试；
- LocalHost、ChatUI lease、FightUI ownership、network teardown 测试；
- 依赖旧私有类名和生命周期步骤的源快照断言。

更新或删除：

- `docs/AuraToolsExp/match-records-replay-export-and-analysis.md` 中的 v8/AVI 描述；
- `docs/AuraToolsExp/version-compatibility-contract.md` 中旧回放兼容声明；
- `AuraToolsExp/protocol.compatibility.json` 的 8/8 清单；
- `tools/Test-AuraToolsExp.ps1` 的旧协议、配置和 AVI 断言；
- 测试项目中对旧 Playback/Media 文件的 Compile Link；
- 发布包中的旧临时目录说明、旧编码器声明和失效 DLL 断言。

### 11.4 数据残留清理

经迁移报告和清理授权后处理：

- 旧 `replay_chunks` 和旧 initial/metadata payload；
- `ExportJobs/current.json` 及其临时文件；
- `Temporary/export-*`、JPEG spool、WAV、`.tmp.mp4` 和半成品 AVI；
- 无引用附件、孤立 Import、孤立 Media 和失效数据库行；
- 旧备份保留策略外的数据库备份。

所有删除目标必须先解析为 MatchRecords owner-system 根目录内的绝对路径，并按
扫描报告逐项执行。未知文件进入隔离区，不能递归清空整个数据根。

## 12. 架构防回归门禁

新增路径级依赖规则：

- `Replay/Core` 禁止 `UnityEngine`、`Witch`、`Mirror`、文件系统和进程 API。
- `Replay/Presentation`、`Replay/Runtime`、`Replay/Export` 禁止
  `FightManager`、`FightInit`、`FightUI`、`EnemyManager`、`RoleTable`、
  `LobbyManager`、`ChatUI`、`NetworkServer`、`NetworkClient`、RPC 和脚本执行器。
- `Replay/Capture` 可以读取游戏对象，但禁止引用 Presentation、Export 和旧
  LegacyMigration 播放入口。
- AuraToolsExp 全局继续禁止依赖 Terrias 实现和私有资源目录。
- 生产代码禁止 `ScreenCapture.CaptureScreenshotAsTexture`、`AudioListener` 录音、
  PATH FFmpeg 搜索、AVI writer 和 v8/v9 runtime negotiation。

行为测试覆盖投影、哈希、包安全、恢复和真实运行结果；架构门禁只负责依赖方向，
不以私有类名或方法顺序替代行为测试。

## 13. 实现任务与单次切换

实现可以拆成以下工作包，但都在同一切换版本完成：

1. **Contract/Core**：v10 模型、规范化序列化、哈希、validator、projector。
2. **Capture/Assets**：录制适配器、实体目录、展示快照、附件采集和最终化。
3. **Presentation**：独立 Scene、Actor/Card/Hud、交互时钟和跳转。
4. **Export**：固定时钟、RenderTexture、帧管道、音频混合、受控 FFmpeg。
5. **Storage/Recovery**：新表、任务状态机、包导入提交和启动恢复。
6. **Migration**：只读扫描、分类、v10 转换、摘要和清理授权报告。
7. **Cutover/Cleanup**：删除旧代码、配置、测试、文档、发布断言和数据残留。

开发期间新实现不得通过发布配置对用户可见；旧实现也不得在最终版本中作为隐藏
fallback 保留。

## 14. 验收矩阵

### 纯逻辑

- 相同 v10 输入在重复运行、不同文化区域和不同机器上得到相同逻辑哈希。
- 从头投影、任意检查点恢复和随机跳转得到相同状态。
- 缺事件、乱序、重复实例、错误哈希和未知必需类型全部被拒绝。

### 自包含包

- 在没有原 MOD 和对应 CSV 的干净机器上可完整播放。
- 删除或修改任一附件、chunk、manifest 字段后导入失败。
- zip traversal、重复 entry、解压炸弹和超限媒体被拒绝且不留下 Ready 文件。

### Unity 游戏内

- 真实游戏完成播放、暂停、变速、动作跳转、回合跳转和退出。
- 播放前后 Mirror、FightManager、ChatUI、RoleTable 和当前冒险状态完全不变。
- 连续进入/退出、多次播放和场景切换无残留对象、监听器或资源租约。

### 视频

- 交互和导出在指定时间点产生相同 ReplayViewState。
- 720p30、1080p30、有/无 HUD、有/无音频均完成完整解码验证。
- 长回放受有界内存约束，不生成 JPEG spool，不依赖实时帧率。
- 编码器缺失、哈希错误、进程失败、磁盘不足和取消只留下可恢复任务记录。

### 恢复与迁移

- 对每个任务状态实施进程终止测试并验证启动恢复结果。
- DB 有/无文件、文件有/无 DB、staging/target 各组合均有确定结果。
- v8/v9 扫描报告的计数、ID、字节数和删除范围可重复。
- 不可迁移录像保留统计和冒险关联；未经授权不删除统计。

### 发布

- 源码、测试、配置、协议清单、文档和发布 DLL 全部只声明 v10。
- 发布包包含唯一受控编码器、哈希清单和许可证材料。
- 搜索和架构门禁证明没有旧 LocalHost/FightUI/ChatUI/AVI/PATH FFmpeg 路径。
- 重建并验证 `AuraToolsExp/Scripts/Entry.dll`，再执行真实 Unity 集成验收。

## 15. 完成定义

只有在 v10 录制、验证、交互播放、跳转、退出、离线视频、恢复、迁移扫描、旧
实现删除、数据清理、文档和发布产物全部完成后，回放重建才算完成。任何仍能
启动旧本地主机播放器、执行旧协议、生成 AVI、读取当前 `Level.EnemyIds` 或从
PATH 寻找 FFmpeg 的路径，都意味着切换尚未完成。
