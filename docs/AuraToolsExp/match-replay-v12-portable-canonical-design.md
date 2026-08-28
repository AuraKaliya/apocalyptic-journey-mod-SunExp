# AuraToolsExp 对局记录与独立表现回放 v12 实现架构

状态：v12 代码与持久化主路径已完成切换。v11 writer/player/importer 已删除并自动降级为
`SummaryOnly`；发布前仍需在游戏内完成单机、2/3 人联机和无源 MOD 黑盒验收。

已落地的实现包括纯 reducer、双 journal lane、因果事务与稳定屏障、三层根哈希、配对检查点、
便携 descriptor/asset 冻结、独立 ReplayScene、POV sidecar、SQLite v9、v12 包、固定时间步 MP4、
联机能力协商与 host canonical 复制。自动行为测试、Release 编译和产品发布门禁均已通过；门禁
会拒绝任何 v11/native player 残留、特定内容 MOD 分支，以及 Playback
对 `FightManager`、`FightUI`、`RoleTable` 或玩法脚本的引用。

## 1. 已批准的产品契约

1. `Replay Document v12` 是下一代唯一结构化记录格式，`.aurareplay` 包版本固定为 12。
2. 单机由本机写入权威日志；联机由主机分配全局顺序并封存唯一 canonical 文档。
3. 正常完成复制后，各联机节点持有相同的 canonical 文档、事件链和根哈希。
4. 本机手牌、私有牌堆顺序和明确纳入 POV 契约的私人牌区表现进入可选 POV sidecar。
   canonical 文档既不包含 sidecar 正文，也不包含 sidecar 目录、引用或哈希。
5. 结构化回放不要求录制时的内容 MOD、DLL、注册表或资源包仍然安装。
6. 录制端可以观察游戏主体的战斗对象、UI 和网络边界；播放端不得依赖游戏主体的战斗
   初始化、战斗单例、玩法对象或内容表，只依赖 AuraToolsExp 自有的 Replay Presentation
   ABI、Unity 渲染能力和记录内嵌的便携表现资产。
7. 播放端必须自行初始化专用回放场景、相机、Canvas、背景、HUD、对战双方、卡区、动作
   步骤、卡牌表现、动画、特效和音频，并在退出时只清理自己拥有的对象。
8. 播放器不得执行卡牌、Buff、职业、遗物、敌人、Partner 或内容 MOD 脚本，也不得运行
   AI、随机数、Command、RPC、奖励、存档或任何游戏规则推导。
9. 每个公共状态增量、实体变化和表现消息必须属于同一个有序因果事务；回放只能投影已
   记录的状态和表现，表现消息不能修改权威状态，状态差异也不能反推未记录的表现。
10. 对局记录使用严格白名单，只保存完成确定性状态投影、表现还原、定位和完整性校验所
    必需的通用字段。内容值可以来自任意 MOD，但协议类型和播放原语不得依赖特定 MOD。
11. `Ready` 表示 canonical、全部必要表现描述、全部必要资产、检查点及哈希均完整。无法
   自包含的记录必须成为 `Rejected`，不能在播放时临时依赖源 MOD 或猜测表现。
12. v11 全部退出结构化可播放集合。数据库只保留摘要、分析、收藏信息和已验证 MP4；
   v12 不提供 v11 播放器或双协议运行路径。
13. 通用捕获保证覆盖通过游戏主体战斗表面形成的内容：`FightObject`、`StatusManager`、
    `CardItem`、`FightUI`、`ActionCommand`、`EffectManager` 及其原生管理器。内容 MOD 自建
    相机、独立 UI 或任意自定义 Unity 执行组件不属于游戏主体通用回放契约。

## 2. 为什么必须替换 v11

v11 的根本问题不是字段不足，而是权威所有者和播放依赖错误：

- 本地卡牌、远端卡牌、敌方意图和表现 Hook 分别创建事件，没有共享的全局 action 身份；
- 动作通过固定等待帧数抓取 after-state，没有权威状态版本屏障；
- 播放时间轴丢弃录制时序，重新生成固定时长和固定间隔；
- 远端 `UseCard`、`ActionAnimation` 和 `StatusDataTransfer` 没有可靠的因果关联；
- 实体只在初始基线构造，中途新增 status 没有可执行的 spawn/despawn 生命周期；
- 当前播放器按实时 ID 查询 `DataConfig`，并调用 `Enemy.Init`、`Partner.Init`；后者会执行
  `InitScript`，既依赖源 MOD，也违反只读回放契约；
- exact runtime fingerprint 把未使用的内容程序集也变成播放门禁。

缺失的全局顺序、动态实体配方和远端表现事实无法从旧文档确定性反推，因此不能把 v11
原地补字段后称为 v12。

## 3. 总体数据流

```text
真实战斗原生边界
  -> Native fact observers
  -> Local transaction / entity / state / presentation observations
  -> AuraTools replay authority protocol
       单机: local authority
       联机: host observes sender-bound public commands/status -> validate/order/commit
  -> Causal Transaction Journal
       authoritative public-state deltas
       ordered presentation messages
  -> Reducer-derived full checkpoints
  -> Portable Presentation Capsule + content-addressed assets
  -> truthRoot + presentationRoot + documentRoot
  -> MatchRecordDatabase v9 / .aurareplay v12

Replay Document v12
  -> pure validation and state reduction
  -> AuraTools-owned ReplaySceneRuntime
  -> recorded transaction-step scheduler
  -> replay-owned camera / canvas / background / HUD / combatants / cards / effects
  -> interactive replay or fixed-frame MP4 export

Local private observations
  -> POV sidecar(parentDocumentRoot)
  -> optional POV playback overlay
```

canonical journal 是唯一战斗事实来源。完整检查点必须由同一个 public-state reducer 生成，
不能成为第二份独立捕获的事实。分析、资料库摘要、MP4 和 POV 都是引用 documentRoot 的
派生数据，不能反向修改 canonical。

### 3.1 采用的回放组合

v12 明确采用以下组合，而不是重新运行一次战斗：

- 学 MD：同一个因果事务拥有该行为的全部公共状态步骤和表现步骤；
- 学 CS2：权威 public-state delta 与 presentation message 分 lane 保存，并用完整检查点定位；
- Aura 约束：不执行任何玩法脚本，只由纯 reducer 投影记录状态，由独立 ReplayScene 播放
  已记录表现。

录制端负责观察“发生了什么”，播放器负责展示“记录说发生了什么”。播放器不判断卡牌
是否合法、不计算伤害、不触发 Buff，也不从最终差异猜测中间动作。

## 4. 协议和哈希域

### 4.1 固定版本

| 契约 | 版本 |
| --- | ---: |
| Replay Document | 12 |
| `.aurareplay` package | 12 |
| SQLite `user_version` | 8 |
| Canonical journal network protocol | 1 |
| Replay Presentation ABI | `aura-replay-presentation.v1` |
| Timebase | 1,000,000 ticks/second |

网络协议和持久化协议独立演进。一次网络协商失败只关闭本场结构化回放，不得把整个
AuraToolsExp 或大厅提升为全局同版本要求。

### 4.2 Canonical header

`ReplayDocumentHeaderCoreV12` 至少包含：

- document/minimum-readable/package 版本；
- host 生成的 record、battle session、adventure、level 和 outcome 身份；
- host 生成并广播的开始/结束 UTC，仅作显示；
- 游戏 build 和来源版本仅作 provenance；AuraTools recorder build 和 Replay Presentation ABI
  用于播放器兼容性；
- required/optional capability 列表；
- initial/final public state hash、entity descriptor catalog、asset manifest、journal chunk list、
  checkpoint list、`truthRoot` 和 `presentationRoot`。

`documentRoot` 不写入被自身哈希的 header core。存储层使用
`ReplayDocumentEnvelopeV12(headerCore, declaredDocumentRoot)`，其中 declared 值必须等于对
canonical header core 计算得到的 root，避免自引用哈希。

### 4.3 不进入 canonical 哈希的内容

- 本机绝对路径、文件时间和数据库 sequence；
- 本机已加载 MOD 列表、程序集 MVID 和机器特征；
- 本机 UI 搜索、分页、滚动位置和回放设置；
- 全部 POV 正文、目录、引用、可用 player id 和 sidecar hash；
- 导出任务、MP4 文件路径和转码进度；
- 捕获端日志、异常栈和本地诊断附件；
- 任意玩法脚本正文、脚本入口、程序集信息和可执行 payload。

`ownerModId`、稳定内容 id 和来源版本可以保留为 provenance/诊断，但播放器不得把它们
解析为运行时依赖。canonical 使用的是记录内内容描述和附件哈希。

### 4.4 三层哈希

- `truthRoot`：因果事务骨架、权威公共状态增量、实体生命周期和完整检查点；
- `presentationRoot`：表现消息、便携描述符和必要资产 manifest；
- `documentRoot`：包含前两个 root 的 canonical header core 的 SHA-256。

三个根都由主机封存并在各节点验证。表现事件属于 canonical 文档，但不拥有或推导公共
状态；`truthRoot` 可以独立证明战斗事实一致，`documentRoot` 证明整份可播放记录一致。
POV sidecar 只保存 `parentDocumentRoot`，任何 canonical 根都不得反向引用 POV。

### 4.5 事件链

每个 `ReplayJournalEventV12` 包含：

- `Truth` 或 `Presentation` lane；
- 严格递增的 host `sequence` 和稳定 `eventId`；
- `roundSequence`、`actorTurnSequence`、必需 `transactionId`、`stepOrdinal` 与可选
  `causeEventId`、`parentTransactionId`；
- canonical logical time 和同一时间内的稳定 ordinal；
- authority kind、绑定后的 issuer player id、actor status id；
- typed payload；
- state 事件必需 before/after public state hash，纯表现事件不得改变这两个哈希；
- previous lane event hash 和 event hash。

host `sequence` 在两条 lane 间全局唯一，lane 内 hash chain 分别验证。validator 按 sequence、
transactionId 和 stepOrdinal 合并两条 lane，并拒绝重复 sequence、事务步骤倒退或无对应
truth transaction 的表现事件。

事件序列化必须使用纯 .NET canonical JSON：对象字段固定、字典按 ordinal 排序、集合在
模型要求的位置显式排序、整数时间基准、浮点值写入 IEEE 位或量化整数。

## 5. Canonical 公共状态与数据最小化

### 5.1 `ReplayPublicStateV12`

权威状态只保存所有观察者一致、并且播放器或完整性验证实际需要的数据：

- 战斗 phase、round、actor turn、当前行动者、队伍、胜负结果和稳定状态版本；
- 实体 id、spawn generation、team、owner、slot 和存在/存活状态；
- HP、最大 HP、防御及游戏主体 HUD 已公开展示的标准数值；
- Buff/状态实例的 descriptor ref、层数、可见持续信息和稳定排序；
- 敌方或 Partner 已公开的 intent descriptor ref、目标和顺序；
- 公共卡牌实例、来源、已使用/展示/弃置/消耗等公共卡区和卡区计数。

私人手牌身份、私人牌堆顺序和仅本机可见的卡牌变量不属于 `ReplayPublicStateV12`。canonical
只保存公共卡区事实和公共计数；完整私人牌区只能进入 POV sidecar。

扩展公共字段必须同时声明稳定语义、录制 producer、播放或验证 consumer、canonical 编码和
validator。缺少任一项的字段不得加入 v12。

### 5.2 明确排除

canonical 和便携表现层都不得保存或反序列化：

- 完整 `DataConfig.data`、`Vars`、动态变量字典或反射抓取的对象图；
- 任何 Init/Use/Clear/事件脚本正文、脚本入口或可执行表达式；
- `ScriptExecutor`、AI、随机数生成器、Command/RPC、NetworkIdentity 或 manager 单例状态；
- 任意 MonoBehaviour、组件、delegate、Tween、协程或运行时对象引用；
- 未被播放器或 validator 消费的诊断字段。

内容专用的名称、图片、动画帧和颜色可以作为通用 descriptor 的值保存。通用性要求的是
schema 和播放器原语不认识特定 MOD，而不是删除重现表现所必需的内容值。

### 5.3 Typed state delta

`ReplayStateDeltaV12` 只能包含版本化的 typed operation，不使用任意 JSON Pointer、字段名或
字典 patch。v12 初始 operation 集至少包括：

- `SetBattlePhase`、`SetRoundTurn`、`SetActiveActor`、`SetOutcome`；
- `SetEntityVitals`、`SetEntityPresence`；
- `ReplaceVisibleBuffs`、`ReplaceVisibleIntents`；
- `AddPublicCard`、`MovePublicCard`、`RemovePublicCard`、`SetPublicZoneCount`。

每个 operation 都有固定字段、顺序和边界校验。新增 operation 必须提升相应 capability 或
document 版本；不得用 `CustomField`、字符串路径或 opaque payload 绕过白名单。

## 6. Canonical 因果事务模型

### 6.1 事务类型

所有状态变化、实体变化和表现消息都必须属于 `ReplayCausalTransactionV12`。事务类型至少
包括：

- `Bootstrap`：物化初始公共状态和初始表现；
- `Card`、`Skill`、`Intent`：卡牌、技能和敌人/Partner 行动；
- `Passive`：Buff、遗物或其它被动触发；
- `SystemPhase`：FightStart、RoundStart、TurnStart、TurnEnd 和结算阶段；
- `Spawn`、`Despawn`、`Transform`：独立实体生命周期和形态变化；
- `Outcome`、`Cleanup`：结果进入和终局清理。

父事务触发被动、召唤或其它行动时，子事务通过 `parentTransactionId` 关联。生命周期标记
本身可以没有状态 payload，但它仍位于对应的 Bootstrap、SystemPhase、Outcome 或 Cleanup
事务内。不存在 `StandaloneStateSettled` 或其它脱离事务的状态写入路径。

### 6.2 有序步骤

一个事务包含严格递增的 `stepOrdinal`，可以多次交错出现：

- `SourcePresented`：卡牌、技能、意图或其它动作来源；
- `ActorAnimationPresented`；
- `EffectPresented`、`HitReactionPresented`、`AudioPresented`；
- `EntitySpawned`、`EntityDespawned`、`EntityPresented`、`EntityPresentationChanged`；
- `StateDeltaApplied`；
- `TransactionCompleted` 或 `TransactionAborted`。

Truth lane 保存事务开始/结束、生命周期标记、`EntitySpawned/Despawned` 和
`StateDeltaApplied`；Presentation lane 保存 source、animation、effect、hit、audio、
`EntityPresented` 和 `EntityPresentationChanged`。两个 lane 的事件引用同一 transactionId
和 stepOrdinal，只有 host authority 可以分配最终 stepOrdinal。

一个多段行动可以拥有多次 `StateDeltaApplied`。每次 delta 都携带 before/after public state
hash，并在其录制 step 到达时投影；因此多段伤害、召唤后行动和被动连锁不必等待整张卡牌
结束后一次性跳到终态。事务只能有一个最终完成或中止事件。

播放时不得从 before/after 差异反推卡牌、动画、命中特效或时序；表现均来自录制时观察到
的消息。反过来，表现消息也不得修改 reducer 状态。

### 6.3 实体顺序

实体第一次完成游戏主体 status、owner/manager 和表现索引物化后，录制端在所属事务提交
`EntitySpawned`，payload 只包含初始 public entity state。随后在 presentation lane 提交
`EntityPresented(entityId, generation, descriptorId)`。两者必须早于任何引用实体的 target、
Buff、intent 或动作表现。原生移除生命周期完成后提交 `EntityDespawned`。Spawn/Despawn
都是带 before/after state hash 的 typed truth operation，不再额外生成重复的 Add/Remove
entity delta。

`EntityPresentationChanged` 只用于完整动画集、皮肤、形态或持续布局确实改变的情况；HP、
防御、Buff 和普通状态变化继续使用 `StateDeltaApplied`。这些原生 manager 条件仅约束录制
端事实成熟度，播放端不创建或注册对应游戏 manager。

### 6.4 完整检查点

`ReplayTruthCheckpointV12` 必须由已经提交的 canonical journal 通过同一个纯 .NET reducer
确定性生成，不得另行读取实时对象后形成第二份状态事实。它完整保存：

- 当前 `ReplayPublicStateV12`；
- 活动实体 id 和 spawn generation；
- 公共卡区、Buff、intent、round/turn/outcome；
- 最后已应用的 truth event sequence、event hash 和 state hash。

`ReplayPresentationCheckpointV12` 也必须由 presentation reducer 确定性生成，保存同一稳定
边界的持久表现投影，包括 scene descriptor、实体 binding/generation 和当前稳定动画状态。
HUD 由同序列 truth checkpoint 重建。检查点不保存活动 Tween、瞬时粒子或正在播放的音频；
定位到事务中间时，从最近稳定检查点静默归约后续事件，并扫描 presentation lane 重建目标
时刻仍有效或尚在 delay 中的卡牌、效果和音频 cue。

检查点不得包含 POV 引用、sidecar hash 或私人牌区。完整检查点至少出现在：

- Bootstrap 事务完成后；
- 每个 RoundStarted 对应 SystemPhase 事务完成后；
- 每个包含实体 spawn/despawn 的事务完成后；
- 每 64 个已完成事务；
- Outcome 和 BattleFinalized 对应事务完成后。

定位时从最近检查点恢复，再静默应用 journal；不得调用脚本或从当前内容表补数据。

## 7. 因果事务身份和收口屏障

### 7.1 身份

事务源节点创建：

```text
sourceToken = battleSessionId + ownerPlayerId + ownerTransactionSequence + randomNonce
```

联机主机从 Mirror receive context 绑定真实 sender，验证 sender 拥有 actor status 后分配：

```text
transactionId = battleSessionId + hostGlobalTransactionSequence
```

payload 中的 issuer、owner 或 actor 不能用于授权。重试使用同一 sourceToken；主机按
`battleSessionId + boundSender + sourceToken` 幂等。

### 7.2 事实来源和因果上下文

事务协调器组合以下原生事实，而不是让每个 Hook 独立写文档：

- `AuraCardActionTransactionRouter` 的 Attempting/NativeStarted/Committed/
  PresentationCommitted/Completed/Aborted；
- `SkillItem.TrueUse`；
- `FightManager.DoAction(FightObject)` 的 actor-turn 包络；
- `OtherObj.DoOneAction` 的原生意图槽；
- `FightUI.DoCardUseAnimation`、`CallActionAnimation`、`DOActionAnimation`；
- `ActionCommandBase.Execute` 的远端公开命令；
- status command、`StatusDataTransfer.Populate`、Buff/变量和实体索引观察；
- card/skill/intent source 完成、权威 status 到达、必需资产完成和战斗生命周期边界提交的
  稳定屏障请求。请求由 `AuraSharedFrameScheduler` 按 record owner/key 合并到下一帧执行；
  Recording 不再 Hook `FightManager.Update`，空闲帧不得扫描完整战斗状态。

这些类型和方法只允许出现在 Recording/native-observer 边界。Playback 项目不得引用它们。

协调器为当前 native command/executor 建立 `ReplayCausalContext`，将嵌套事实绑定到当前事务或
显式子事务。现有共享 card transaction 仍服务 CG、音频和 Terrias 行为；Replay v12 只把
它视为一种事实来源，只有 host journal authority 可以分配 step 并提交 canonical 事件。

### 7.3 隐式原生事务

若捕获端观察到 `FightUI.CallActionAnimation` 或远端 `ActionAnimation`，但没有已知 card、
skill 或 intent 事务，协调器从原生 executor/status、允许的 provenance 和 targets 创建
`ImplicitNativeTransaction`。其 before-state 是上一个已提交稳定状态，后续事实通过当前
`ReplayCausalContext` 和显式稳定屏障归属，而不是仅凭相邻帧猜测。

该规则覆盖使用游戏主体表现接口、但不经过玩家 `TrueUse` 的 Partner 或内容 MOD。它不是
Terrias 特例，投影只是该规则的验收样本。无法绑定到唯一开放事务的表现事实仍必须成为
`Rejected: ambiguous-causal-ownership`，不得按时间接近程度猜测。多个已经完成的并列源事务
可以在同一个显式稳定屏障中一起关闭：源事务结束时已经捕获各自直接状态；下一帧才出现的
残余公共状态由独立 `Passive/StableBarrier` 系统事务承接，不伪造为任意一个兄弟事务的状态。

### 7.4 收口条件

不得用“延迟两帧”“状态连续两次不变”或超时作为成功收口。事务只有在以下责任全部完成
后进入 `TransactionCompleted`：

1. 源事务已 Committed/Completed，或隐式事务已经收到完整 native presentation cue；
2. 主机已处理该事务之前提交的原生 command/status 队列；
3. 该事务的每次公共状态变化均已形成有序 `StateDeltaApplied`；
4. 受影响 status 的主机权威版本水位已进入 canonical state；
5. 实体增删、形态变化和公共卡区写入均已物化；
6. 必需表现消息、portable descriptors 和资产均已封存或验证上传完成。

未完成事务由 `CanonicalTransactionLedger` 持久追踪。owner 是 host replay authority；drain
trigger 是合并后的事件驱动稳定屏障、缺失事实/资产到达或 `BattleFinalized`。联机能力收据
通过 capability-changed 事件唤醒延迟基线提交，不依赖逐帧轮询。终局仍未完成时写
`TransactionAborted` 并使整份结构化记录成为 `Rejected`，不能制造默认动画、空目标或合并
终态来通过验证。

### 7.5 性能不变量

- Recording 不注册 `FightManager.Update` 或其它逐帧完整状态观察器；
- 同一帧的多个动作、权威状态和资产完成请求合并为一次稳定屏障；
- 稳定屏障只在存在责任时捕获公共状态，空闲战斗帧分配量为零；
- 每场记录输出 requests、runs、stateChanges、totalMs 和 maxMs，单次超过 8ms 输出有界慢日志；
- JSON 规范化、哈希、压缩和数据库写入继续在不可变快照离开主线程后执行。

## 8. 联机权威与复制

### 8.1 能力协商

进入战斗前确认所有当前房间节点支持 `records.match-replay` network protocol 1 及必要
capabilities。任一节点缺少能力时：

- 本场不发送 replay RPC；
- 只生成本地摘要/分析；
- 不启动一个“尽量录”的非权威结构化分支。

这只影响结构化回放，不影响其它 AuraToolsExp 功能。

### 8.2 主机 authority runtime

主机拥有：

- recordId、battleSessionId 和 global sequence；
- sender-bound 远端公共命令验证、actor/entity 映射和 transaction ledger；
- canonical entity/public-state projection；
- truth/presentation journal chunk、asset manifest、checkpoint 和三个 final root；
- `Ready`/`Rejected` 最终判定。

敌人、原生 Partner、主机权威合成单位和远端玩家公共动作都由主机观察。远端卡牌/技能通过
Aura 共享的 sender-bound remote-combat observation router 到达主机；主机把 lobby/player
身份映射到 canonical entityId，并只以主机状态表生成 after-state。客户端不提交
`StateDeltaApplied`，也不成为第二 writer。

卡牌来源、动画 cue、target、便携 descriptor 和资源由主机在接收公开命令及原生
`FightUI/StatusManager` 表现时冻结。联机玩法本身要求主机能解析该场战斗内容，因此 v12 不再
额外建立一条“客户端上传可执行表现”的协议；这避免同一动作出现主机事实与客户端事实两套
来源。无法由通用白名单归一化的必需表现会使记录 `Rejected`。

### 8.3 资产传输

最终 canonical transfer 携带主机已经封存的 portable asset payload set：

- SHA-256 先行去重；
- transferId、chunk index/count、总字节、SHA-256、TTL 和 sender-bound owner；
- 单 chunk 默认不超过 64 KiB；
- 单节点同时活动传输有固定上限和 TTL；
- payload set 必须与 canonical asset manifest 完全相等，完整校验后才进入 asset store；
- 不传 DLL、脚本、绝对路径或可执行代码。

大资产传输可以跨帧和后台做纯 IO/压缩，但 Unity 纹理/音频读取必须在主线程调度并使用
generation 检查。

### 8.4 Canonical 复制

truth lane 和 presentation lane 在持久化与包内分别以约 256 KiB 压缩目标形成不可变 chunk。
每个 chunk 包含 lane、index、sequence 范围、前一 chunk hash 和自身 SHA-256，两条 lane 通过
相同的 transactionId/stepOrdinal 对齐。联机复制在终局把已封存 envelope、检查点和必要资产
组成唯一 canonical transfer，再经 sender-bound 有界传输分片广播；客户端乱序重组后重新验证
两条 lane、全部资产和三个根，随后按同一规则落库。传输分片不是第二套事实格式。

`BattleFinalized` 后主机发布 final manifest。客户端只有在全部 chunk、checkpoint 和必要
asset 以及 truthRoot、presentationRoot、documentRoot 均验证后，才能原子提交相同 canonical
文档。网络中断时 staging 不能提升为 `Ready`；host 退出导致游戏终止时，本地仅保留
`Incomplete`/摘要，不允许客户端接管为第二 writer。

## 9. POV sidecar

`ReplayPovSidecarV12` 包含：

- parent `documentRoot` SHA-256；
- player id 和 POV schema version；
- 对齐 canonical transaction/step 的本机私有牌区状态增量；
- 本机可见手牌、抽牌堆/弃牌堆顺序和只对本机公开的卡牌表现；
- 私有卡牌 descriptor manifest 和内容寻址资产引用；
- sidecar 自身事件链与 SHA-256。

sidecar 不得改变单位 HP、Buff、公共卡牌使用、敌方意图、行动顺序或结果。缺少 sidecar 时
播放器使用 canonical observer view；存在 sidecar 时只叠加该 player 的私有牌区。

sidecar 可以本地保存或在用户显式导出时随包携带。默认网络复制不发送另一玩家的私有
sidecar。数据库和导出包可以在 canonical 之外维护 sidecar 索引；该索引及其变化不得改变
三个 canonical 根。sidecar 的全部私有 descriptor 和资产受 sidecar 自身哈希保护，因此
使用该 POV 回放时同样不依赖源 MOD。

## 10. Portable Presentation Capsule

### 10.1 场景与 UI 描述

`ReplaySceneDescriptorV12` 为独立播放器提供初始化所需的全部通用表现输入：

- 参考分辨率、坐标系、宽高比策略和 safe-area 规则；
- 背景 texture/sprite、clear color、sorting 和可选静态装饰层；
- camera projection、位置、orthographic size/FOV 和 viewport；
- 双方 team/slot anchor、combatant bounds、卡区和飞牌路径 anchor；
- HUD、HP/防御、Buff、intent、回合、结果和控制层的 Aura replay layout profile；
- 字体、图标和颜色 replay profile 的安全引用。

descriptor 不保存游戏 Scene 名、GameObject path、原生 UI prefab id 或组件字段。标准布局由
Replay Presentation ABI 提供；只有录制时实际不同且播放器必须复现的值才进入 descriptor。
所有位置和路径参数必须使用量化 Replay Scene 坐标或语义 anchor，不保存分辨率相关的本机
屏幕像素。

### 10.2 实体描述

`ReplayPortableEntityDescriptorV12` 至少保存：

- canonical descriptor id；
- `PlayerCombatant`、`EnemyCombatant`、`AlliedCombatant`、`NeutralCombatant` 等与本机 POV
  无关的表现 archetype；
- name、subtitle、原始 content id 和原始 DataType（仅 provenance）；
- 被初始表现、checkpoint 或 journal cue 实际引用的规范化 animation state 及其有序 sprite
  frame 引用；
- sprite name、rect、pivot、border、pixels-per-unit 和纹理 SHA；
- 可选的安全 action/effect display profile。

实体实例级表现由 `ReplayEntityPresentationBindingV12` 保存：entity id、spawn generation、
descriptor id、语义 layout anchor、量化 offset/scale、sorting、flip 和颜色。该 binding 由
`EntityPresented` 创建，由 `EntityPresentationChanged` 版本化替换；team、owner 和 slot 仍
只来自 public state。

捕获直接读取已经物化的 `StatusManager`、`fatherObject.AnimatedStateSprites`、Renderer 和
原生 UI 输入，再投影到上述白名单。不得保存本地 runtime instance id，不得根据 id 重新
查询内容表来“补齐”。HP、防御、Buff 层数等事实来自 `ReplayPublicStateV12`，不能在表现
descriptor 中维护第二份状态。

### 10.3 卡牌与 Buff

`ReplayCardDescriptorV12` 只保存 canonical descriptor id、名称、说明、DataType provenance、
显示费用格式、卡图、主题、卡框、字体/颜色 profile 和 Aura-owned 安全动态效果参数。卡牌
实例、卡区和顺序属于 public state 或 POV，不在 descriptor 中重复保存。

不保存运行时表现字典、脚本字段、executor 所需字段或未被 `ReplayCardView` 消费的键。

`ReplayBuffDescriptorV12` 和 `ReplayIntentDescriptorV12` 只保存 descriptor id、名称、说明、
图标、层数/目标显示格式和安全颜色 profile。这里的格式只控制文本与图标，不表达 Buff 或
意图行为。播放端不得调用 `new DataConfig(buffId, DataType.Buff)` 或实时资源注册表。

### 10.4 效果和材质

便携保证只覆盖能够归一化到播放器安全表现原语的结果：

- 游戏主体已知 EffectManager 类型和目标模式；
- sprite/texture sequence；
- AuraTools 自带的安全 replay shader/material profile；
- 量化的颜色、UV、透明度、持续时间、位置、缩放和排序参数；
- 内容寻址音频。

播放器不加载来源 MOD DLL，也不实例化未知 MonoBehaviour。遇到无法归一化的必需自定义
shader、组件或效果时，捕获结果为 `Rejected: portable-presentation-unsupported`。这保证
所有 `Ready` 记录都真正脱离源 MOD，而不是在播放时悄悄降级。

### 10.5 可达性裁剪

录制期间允许在 staging 中暂存候选 descriptor 和资产。`BattleFinalized` 后从 scene
descriptor、presentation checkpoint 和全部 cue 建立引用图，只把可达节点写入 canonical
presentation manifest：

- 不打包未出场实体、未展示卡牌、未出现 Buff/intent 或未播放效果；
- 不打包未被引用的 animation state、sprite frame、texture 或 audio；
- 相同内容按 SHA-256 去重；
- 公共记录与 POV 分别执行可达性裁剪，不把私人未使用内容提升到 canonical。

裁剪完成后 validator 反向检查每个必需引用均可解析。缺失引用为 `Rejected`，额外不可达
附件不得进入 `Ready` 包。

## 11. 独立表现播放器

### 11.1 场景与所有权边界

`ReplaySceneRuntime` 创建并拥有专用 Unity Scene 或等价的完全隔离根对象。它不要求真实
战斗 Scene 已加载，也不安装临时 `RoleTable`、`FightManager`、`FightCardManager` 或
`FightUI.Instance`。全部运行时对象由 AuraToolsExp 类型构成：

```text
ReplaySceneRuntime
ReplayAssetCacheV12
ReplayCombatantViewV12
ReplayHudRuntimeV12
ReplayCardPresenterV12 / ReplayPovHandRuntimeV12
ReplayEffectRuntimeV12 / ReplayAudioRuntimeV12
```

场景运行时自行创建 Camera、Canvas、背景、双方布局、状态栏、Buff/intent UI、卡区、结果
界面和回放控制层。它可以使用 Unity 的被动渲染组件，以及 AuraToolsExp 自带或记录内嵌的
sprite、texture、font、audio 和安全 material profile；不得调用游戏战斗 UI 的行为方法。

严禁调用：

- `FightManager.Init`、`FightInit.Init`；
- `Enemy.Init`、`Partner.Init`、`OtherObj.SetAction`；
- `FightUI` 的创建、单例、卡牌、动画、布局或效果方法；
- `RoleTable`、`FightManager`、`FightCardManager`、游戏 status/owner/action queue 的注册或修改；
- 以 live id 构造的 `DataConfig(id, DataType)`；
- `RunScript`、Buff Init/ClearScript、职业/遗物/祝福初始化；
- NetworkIdentity、Host/Client、Command/RPC。

`Playback` 程序集或命名空间必须有架构测试，禁止引用上述战斗类型。捕获端对 `FightUI` 等
原生表面的 Hook 不构成播放依赖，两侧只能通过纯 v12 文档模型通信。

### 11.2 状态投影与 View factory

`ReplayStateReducer` 只应用 checkpoint、`EntitySpawned/Despawned` 和 `StateDeltaApplied`，
生成不可变 public state。`ReplayViewBinder` 将该状态绑定到 AuraToolsExp-owned views。
`ReplayEntityViewRegistry` 使用 canonical entity id 和 spawn generation 管理动态
spawn/despawn：`EntityPresented` 到达时用 descriptor 创建 view 并绑定已有 state，
`EntityDespawned` 到达时释放 view。它不借用源 MOD 类型或游戏 owner map。

表现消息只能命令 view 播放已记录的卡牌移动、动画、特效和音频。任何 view callback、Tween
完成或动画结束都不能写回 reducer，也不能触发额外伤害、Buff、抽牌或行动。

### 11.3 Transaction-step scheduler

播放器按 journal 中的 logical time、transactionId 和 stepOrdinal 调度 `SourcePresented`、
`ActorAnimationPresented`、`EffectPresented`、`HitReactionPresented`、`AudioPresented` 和
`StateDeltaApplied`。速度切换只缩放 canonical 时间，不重新推导固定 80/180/640 ms 常量。

`MatchReplayPlayer` 与 `ReplaySceneRuntime` 使用记录的 actor/target、animation state 和已捕获 sprite frame
驱动独立 views。结果状态只在对应 `StateDeltaApplied` 到达时投影。任一必需 actor、target、
asset、descriptor 或 cue 缺失时立即停止并报告文档损坏。

### 11.4 定位和清理

定位先取消播放器拥有的 Tween、动画、飞牌、效果和音频，再从检查点重建活动实体集合与
状态，静默应用 journal，最后恢复目标时刻的 HUD。相同目标必须得到相同状态哈希和活动
entity descriptor 集。

所有退出原因进入同一 teardown：释放 card/material/effect/audio lease，销毁 playback-owned
Scene、Camera、Canvas 和对象，恢复进入回放前 AuraToolsExp 资料库 UI 的可见性，再验证下一
Unity 帧无 replay-owned Tween、audio、input、material 或静态引用残留。因为播放器从未修改
游戏战斗单例，所以不存在恢复 `RoleTable/FightManager/FightCardManager/FightUI` 的步骤。

## 12. 存储和包格式

### 12.1 SQLite schema v9

目标表：

- `battle_records`：摘要、状态、协议和资料库元数据；
- `replay_documents`：version 12 canonical header 和三个 root；
- `replay_truth_chunks`、`replay_presentation_chunks`：两条不可变 journal lane；
- `replay_truth_checkpoints`、`replay_presentation_checkpoints`：同一稳定边界的检查点对；
- `replay_assets`、`replay_asset_refs`：内容寻址便携资产；
- `replay_pov_sidecars`、`replay_pov_asset_refs`：通过 parentDocumentRoot 单向引用的本机可选 POV；
- `replay_export_jobs`、`replay_media`：派生媒体；
- `replay_migrations`：一次性迁移账本。

`replay_documents.document_version` 使用 `CHECK(document_version=12)`。v12 运行时不读取
v11 document/chunk 表结构。

### 12.2 `.aurareplay` v12

```text
manifest.json
document.json.gz
timeline/truth/000000.json.gz
timeline/presentation/000000.json.gz
checkpoints/truth/000000.json.gz
checkpoints/presentation/000000.json.gz
assets/<sha256>.png
assets/<sha256>.wav
analysis/summary.json.gz
```

默认可移植包不包含私人 POV 或 MP4；它们继续作为数据库 sidecar/派生媒体独立保存，不进入
三个 canonical root。导入先验证 entry kind 白名单、路径、重复 entry、压缩/解压预算、版本、truthRoot、
presentationRoot、documentRoot、两条 chunk 链、checkpoint pair、asset manifest 和全部必要
附件，再原子提交。包不得包含 DLL、脚本程序集、脚本文本或绝对路径。

## 13. v11 一次性迁移

任意 pre-v12 schema -> 9 执行：

1. 先创建带时间戳的数据库备份并保留现有备份上限；
2. 统计 v11 Ready/Rejected 文档、chunk、asset ref 和字节数；
3. 将所有 v11 结构化记录改为 `SummaryOnly`，保留原始 `replay_protocol=11` 作为历史事实；
4. 保留摘要、分析、收藏/标签/备注和已验证 `replay_media`；
5. 删除 v11 document、timeline chunk、checkpoint/export job 和仅由 v11 引用的附件；
6. 重建 version 12 表；
7. 写入 `replay-v11-to-v12-independent-presentation-cutover` 迁移账本和统计；
8. 执行 FK orphan、asset ref、媒体文件和 SQLite integrity 校验。

旧 `.aurareplay` v11 包不进入 v12 importer，也不保留隐藏转换器。资料库中的 v11 记录只
提供摘要、分析和 MP4 操作。

## 14. 模块所有权

### AuraSharedCore

保留语义无关的生命周期、card transaction、hook registry、frame scheduler、sender binding
和 bounded transfer 基础。现有消费者继续使用 `AuraCardActionTransactionRouter`。除非其它
消费者也需要统一 combat observation，不在 Core 中加入 replay document/storage 语义。

### AuraToolsExp MatchRecords

建议目标目录：

```text
Features/MatchRecords/ReplayV12/Core
Features/MatchRecords/ReplayV12/Recording
Features/MatchRecords/ReplayV12/Network
Features/MatchRecords/ReplayV12/Playback
Features/MatchRecords/ReplayV12/Storage
Features/MatchRecords/Portability
```

- `Core`：纯 .NET public state、因果事务、双 lane、reducer、三层哈希、检查点和 validator；
- `Recording`：native fact adapters、transaction ledger/context、entity catalog 和 state watermark；
- `Network`：能力协商、host authority、sender-bound canonical chunk/asset replication；
- `Recording` 内的 presentation capture：录制端 Unity 资产冻结、白名单投影和 portable descriptors；
- `Playback`：独立 ReplayScene、state/view projection、transaction director、seek 和 teardown；
- `Storage/Portability`：SQLite v9、迁移和 v12 package；
- 现有 `Media` 只通过 v12 player/render surface 消费 canonical 文档。

`Core` 不引用 Unity 或游戏程序集。`Playback` 只能依赖 `Core`、Unity 被动渲染 API、
AuraToolsExp-owned UI/资源基础和 v12 descriptors，不能依赖 `Recording`，也不能引用游戏战斗
manager、FightUI、DataConfig 或玩法对象。该依赖方向由 Release source/assembly gate 约束。

Terrias 不新增 replay API、manifest 或 adapter。Projection 相关代码不被 AuraToolsExp 引用。

## 15. 旧路径删除清单

完成 v12 切换时必须删除或替换：

- `ReplayDocumentV11`、v11 canonical/hash/validator/chunker/migration runtime；
- `MatchReplayRecorder` 的单一 `activeAction`、固定两帧 finalization 和未接入 convergence；
- 仅处理远端 CardUse 的 recorder 分支；
- v11 `ReplayNativeDocumentAdapter` 与 exact runtime fingerprint 播放门禁；
- 通过 live `DataConfig(id, DataType)` 恢复 Enemy、Partner、Buff 的路径；
- `MatchReplayFightSandboxInitializer`、临时 RoleTable/FightManager/FightCardManager、隐藏原生
  FightUI、native status registration 和调用 Enemy/Partner 正常 Init 的 sandbox；
- 固定时长 `MatchReplayPresentationSchedule` 和由差异猜测表现的默认 cue；
- `StandaloneStateSettled`、单一 `ActionStateSettled`、运行时表现字典和脚本文本记录路径；
- v11 SQLite writer/reader、package importer/exporter 和数据库修复分支；
- `protocol.compatibility.json`、设置 UI、测试和文档中的 11/11 声明；
- architecture gate 中只服务 v11 的规则和 retired symbol 清单。

有其它消费者的共享 card transaction 和 remote observation router 不因 replay 切换而误删；
只删除 replay 对旧私有写入路径的依赖。

## 16. 验收矩阵

### 16.1 纯行为测试

- canonical JSON、双事件链、三层 root、asset manifest、checkpoint pair 和 POV 单向 parent；
- transaction ledger 幂等、嵌套、多次 delta、顺序、缺失事实、终局未 drain 和拒绝结果；
- 无唯一因果归属时稳定拒绝，不能按帧或时间邻近猜测；
- public-state 白名单拒绝脚本、任意字典、运行时对象和未声明字段；
- checkpoint 由 reducer 重建，与逐事件归约的 public state/hash 完全相同；
- descriptor/asset 引用图裁剪未使用内容、保留全部必需内容并稳定去重；
- entity spawn/despawn/形态变更投影；
- source MOD provenance 不参与播放依赖；
- v11 -> SummaryOnly 数据库迁移、表删除、asset 清理和迁移账本；
- v12 package 的路径、预算、重复 entry、hash、缺失附件和可执行文件拒绝。

### 16.2 游戏主体捕获集成

- 普通卡、攻击卡、技能、多次使用、回收、燃尽、异步抽牌和回合被动；
- Enemy 多意图、原生 Partner、战中新增/移除 Enemy 与 Partner；
- FightStart 创建实体/Buff、RoundStart 状态变化和 SystemPhase/Passive 事务；
- 多段动作中卡牌表现、actor animation、effect、hit reaction 和多次 state delta 顺序一致；
- 捕获端可以观察 FightUI，但只能输出纯 v12 数据，不能把游戏对象写入文档。

### 16.3 独立播放运行时

- 从资料库直接启动回放时，没有真实战斗 Scene、FightInit 或 FightManager 初始化；
- ReplayScene 自行创建 Camera、Canvas、背景、双方、HUD、卡区、动作、特效、音频和结果 UI；
- Playback 源码/IL 不引用 FightUI、RoleTable、FightManager、FightCardManager、DataConfig、
  Enemy、Partner、StatusManager、CardItem、ScriptExecutor 或 NetworkIdentity；
- 任意 seek 后 public state hash、实体 generation、公共卡区和 HUD 一致；带 POV 时私人牌区
  一致，不带 POV 时 observer view 仍可完整播放；
- view、Tween 或动画完成不会写 reducer，也不会产生未记录的状态或表现；
- 播放/导出退出后无 replay Scene、Camera、Canvas、Tween、material、audio、input 或静态引用
  残留，并且游戏战斗单例从未被修改。

### 16.4 联机

- 2/3 人主客机对局，所有在线节点 truthRoot、presentationRoot、documentRoot 完全相同；
- 主机和每个客户端分别出牌、技能、召唤和结束回合；
- status 延迟、重复/乱序 chunk、资产集合去重、清单不等和超限拒绝；
- payload 伪造 owner/issuer 时使用绑定 sender 拒绝；
- peer 缺少 v12 capability 时只产生摘要，不发送未知 RPC；
- host 中断或 transfer 未完成时不提升 `Ready`。

### 16.5 MOD 无关回归

- 录制时加载一个只通过原生 Partner/Status/FightUI 表面工作的测试内容程序集；
- 记录战中创建友方单位、该单位展示卡牌并执行动作；
- 重启后不加载该程序集，且不初始化真实战斗，仍可由 ReplayScene 完整创建单位、播放卡牌/
  动作/特效并得到相同终态；
- Terrias Projection 作为产品级黑盒样本执行同一流程，但 v12 源码和协议不得出现 Terrias
  类型、id 或分支；
- 必需自定义表现无法归一化时录制明确 `Rejected`，不得在播放时才缺图或静默跳过。

### 16.6 发布一致性

- `protocol.compatibility.json`、源码常量、SQLite CHECK、包 manifest、设置 UI、测试和文档
  只声明 12/12；
- residual search 证明无 v11 player/writer/importer、exact MOD fingerprint gate、正常
  Enemy/Partner Init、原生 FightUI playback、战斗单例 sandbox、固定两帧收口、
  `StandaloneStateSettled` 或 live content lookup；
- AuraToolsExp 构建、共享兼容、网络 authority、SQLite、package、FFmpeg 和产品发布事务通过；
- 所有产品包中的 `Aura.Shared.dll` hash 一致。

## 17. 实施顺序和切换条件

实现按依赖顺序进行，但只在全部完成后切换生产：

1. 建立纯 .NET v12 public state、因果事务、双 lane、reducer、三层 hash、checkpoint 和 validator；
2. 建立严格白名单的 portable descriptors 与资产冻结；
3. 用纯文档 fixture 建立独立 ReplayScene、动态实体、transaction scheduler、seek 和 teardown；
4. 建立单机 capture，并用游戏主体矩阵验证；
5. 建立联机 authority、sender-bound observation、canonical chunk/asset 协议并验证同 hash；
6. 建立 SQLite v9、v11 cutover 和 `.aurareplay` v12；
7. 迁移 Media/Analysis/UI 到 v12；
8. 删除全部 v11 operational surface，更新协议清单和发布文档；
9. 自动验收已完成；继续执行游戏内单机、2/3 人联机、Terrias Projection 与卸载源 MOD 黑盒矩阵。

每一步产生的代码都必须属于最终架构。当前代码只保留 v12 writer/player；发布条件是本章
全部自动门禁与游戏内矩阵通过，不允许重新引入 v11/v12 双 writer、双 player 或隐藏回退。
