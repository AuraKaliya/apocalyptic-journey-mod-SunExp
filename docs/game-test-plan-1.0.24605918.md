# 1.0.24605918 游戏集成验证基线

本清单覆盖 AuraToolsExp、AuraDirector、AuraSkin 与 Terrias 在游戏
`1.0.24605918` 上的高风险交界。自动化验证负责结构、哈希、序列化和权限契约；
下列演出与真实主客机行为仍需在游戏内确认。

## 基线证据

- `Witch.dll` SHA-256：`C8D9B8B0E3B553B01464F6F3909A3C360C19B83BDD7AC0488F18B29631872B68`。
- `AllScripts.dll` SHA-256：`5BBD477595DE375DF06B222A95672B06C4FF9A8BBDC0BF3D4EFE08457C4D7F0D`。
- `AllScripts.cs` SHA-256：`C9A2BD3101A6E016518731FD72C4DB0453C382C30B8D98DB408AE7F3A9568CC9`。
- AuraDirector `ReadyToStartGate.V1` 方法体 SHA-256：
  `5BC8DA8FF9659712B6CA63AC833CF23F00414265BC880444849881B097CE9CB6`。
- 基础知识包：932 个动作、137 个状态、56 个敌人、50 个遭遇；脚本操作
  3448 项，其中 537 项保持“未支持/待验证”标记。
- 原生程序：611 个，program set SHA-256：
  `1BB5FFE8EBFCB87537DAB72A37E86EB317A156196E094218FCDC9A0D5C79685C`。

## 共享皮肤

- 在职业选择界面切换原生皮肤与共享皮肤，确认 `CareerImage` 均按各自 Sprite
  的原生尺寸显示，不继承上一张图片的 RectTransform 尺寸。
- 重复切换职业、皮肤和语言，确认 `preserveAspect` 保持开启，图片不拉伸、不漂移。
- 未选择共享皮肤或资源失效时，确认原生 `CareerImage.SetNativeSize()` 行为不受影响。

## AuraDirector

- 当前版本应报告 `detour-compatible`，并给出能力档案 `ReadyToStartGate.V1`；
  不再以整个 `Witch.dll` 哈希决定兼容性。
- 未列入能力白名单的方法体必须 fail-close 为
  `detour-target-capability-unverified`，但战斗启动流程必须 fail-open，不得卡死。
- V1/V2 底模只有在 artifact/weights 哈希与完整兼容元组同时匹配时才可加载；
  修改 content、rules、native-program 或 feature schema 任意一项都应被拒绝。
- 触发一次带 CG 的开战，原生 `ReadyToStart` 在 hold 释放后只执行一次；卸载
  provider 后无残留 Harmony prefix。

## Projection、Spirit 与 HeartChange 联机行动

- Projection 与 Spirit 本体都以 `Partner` 进入第一个 Enemy 之前，不再创建 turn anchor；
  官方 Partner、Projection、Spirit 均遵循宿主原生 Partner 队列快照。
- Projection 在召唤卡完整结算后深复制玩家当前能量、手牌、等待区、抽牌堆、弃牌堆、
  焚毁区、卡牌运行时数据和附加物。第一回合不刷新能量且不额外抽牌。
- Projection 不展示意图，只有主机运行 Actor 自动决策。目标失效只屏蔽对应候选，
  卡牌或无界面行为失败按更大作用域屏蔽；无合法动作、连续失败 3 次或超时后必须结束，
  不能等待玩家接管。已提交动作不得重放。
- 每次 Projection 卡牌提交、回合完成和回合推进都广播牌局快照；客机只水合状态，
  且卡牌 revision 必须单调，乱序旧快照不得覆盖新状态。客机在伙伴 `TurnIndex`
  推进前不得进入下一行动；断线或丢包时必须由等待上限释放，不能永久卡住。
- Spirit 保留独立属性、生命、资源和意图池，固定显示在拥有者右上角；右侧竖向生命条
  自下而上填充，常驻 Buff 列表隐藏，鼠标悬停时显示游戏原生状态面板。
- Projection 与 Spirit 可以同时存在。Projection 占正式友方阵位，Spirit 使用固定附着位；
  两者都是独立友方目标，且各自在原生 Partner 阶段行动。
- HeartChange 保持原生 Enemy 的对象、位置、队列身份、卡池、冷却和行动次数，不创建
  proxy 或友方槽。原生意图生成后只改写目标：有害行动指向其他未受控敌人，有益行动
  指向玩家、Projection 或 Spirit，Self 行动保持自身；完成一次原生行动后解除控制。
- 重连、重开战斗后不得残留旧 anchor、HeartChange proxy 或重复行动；协议版本不匹配、
  战斗 epoch 失效和投影牌局协议不一致时必须拒绝并记录日志。

## 主客机回归

- 分别覆盖主机一人、主机加一名客户端、两名客户端三个场景。
- 每轮记录 `ActionQueue` status ID、伙伴 revision 和 Projection card-state revision，确认所有节点一致。
- 覆盖官方 Partner、Projection、Spirit、HeartChange 两两组合以及三者同时存在。
- 断线重连后重新建立目录与 revision；不得接受旧战斗或其他玩家的意图。
