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
- 在玩家阶段使用【另一个我】后，主机必须在读取牌组和生成 Actor 前发布同 token 的
  `Reserved` 事务；随后依次发布 `Ready` 与 `Completed/Failed`。分别让 Actor 先生成、
  `PlayerTurnCompleted` 先到达，投影都必须在原快照中的既有 Partner/Enemy 之前自主出牌
  至少一次；下一轮只由原生队列执行。重复、倒退或同 revision 冲突帧不得二次行动。
- 客机 Projection 召唤请求只发送角色、拥有者、token 与牌组哈希。主机必须从该玩家的
  `GameServer.RoleTables[playerId]` 读取冒险牌组，不能读取主机 `FightUI` 或主机牌组。
- Projection 使用原始卡牌 id 与可选永久附件建立新牌局，固定 3 能量、每回合抽 5 张；
  不复制当前手牌、牌区、临时 Vars、临时费用、战斗生成牌、Buff 或动态变量。
- 主机在洗牌前把权威 RoleTable 配方投影为 Actor-safe 牌组；玩家 UI/牌堆/能量依赖卡不得
  进入投影手牌。用只包含【精灵球】【另一个我】【命星】等不安全卡的牌组测试时，投影
  应改用【投影·基础行动】并在召唤当轮攻击一次，而不是静默结束。
- 使用 512 张牌的测试牌组召唤时，不得出现 GZip、分块牌组 RPC 或集中实例化全部卡牌；
  卡牌应在进入手牌后才实例化。主机缺少 RoleTable 时保持该 token 的 `Reserved` 回合事务、
  返回非终态并重试；即使玩家先结束也不得越过事务。连续收到 6 次请求仍缺失时由主机
  返回 `RoleDeckTimedOut/Failed` 终态、释放屏障并只返卡一次；中断全部客户端重试后，
  主机侧 30 秒孤儿预留 TTL 也必须产生同一终态，不能永久卡住战斗；
  基础牌组诊断 hash 不一致时记录日志，但仍使用主机权威 RoleTable，不上传附件或牌组。
- Projection 不展示意图，只有主机运行 Actor 自动决策。目标失效只屏蔽对应候选，
  卡牌或无界面行为失败按更大作用域屏蔽；仍有合法 Actor-safe 卡时 EndTurn 必须非法。
  无合法动作、连续失败 3 次或超时后必须结束，不能等待玩家接管。已提交动作不得重放。
- 每次 Projection 卡牌只发送一个合并行动帧；召唤轮事务、spawn、turn completion 和
  death tombstone 分别发送小型状态帧，内部牌组只存在于主机。客户端按 battleEpoch、
  token、round、order、revision、generation 与四类状态序号去重；完成帧提前到达必须
  立即消费，停滞时限频查询主机状态。
- Spirit 保留独立属性、生命、资源和意图池，固定显示在拥有者右上角；右侧竖向生命条
  自下而上填充，常驻 Buff 列表隐藏，鼠标悬停时显示游戏原生状态面板。
- Projection 与 Spirit 可以同时存在。Projection 占正式友方阵位，Spirit 使用固定附着位；
  两者都是独立友方目标，且各自在原生 Partner 阶段行动。
- HeartChange 保持原生 Enemy 的对象、位置、队列身份、卡池、冷却和行动次数，不创建
  proxy 或友方槽。原生意图生成后只改写目标：有害行动指向其他未受控敌人，有益行动
  指向玩家、Projection 或 Spirit，Self 行动保持自身；完成一次原生行动后解除控制。
- 重连、重开战斗后不得残留旧 anchor、HeartChange proxy 或重复行动；协议版本不匹配、
  战斗 epoch 失效或投影卡牌模型不一致时必须返回分类终态错误并返卡。权限错误不返卡，
  发送结果不确定不得盲目返卡，重复终态结果不得二次返卡。

## 主客机回归

- 分别覆盖主机一人、主机加一名客户端、两名客户端三个场景。
- 每轮记录 `ActionQueue` status ID、spawn generation、四类序号和 Projection `TurnIndex`，确认所有节点一致。
- 覆盖官方 Partner、Projection、Spirit、HeartChange 两两组合以及三者同时存在。
- 断线重连后重新建立目录与 revision；不得接受旧战斗或其他玩家的意图。

## 技能语音与本地卡牌特效

- 主机选择洛奈尔、客机选择乌娜；客机释放 Career `Skill1`【白曜圣祷】时，本地与远端
  各播放一次“愿白昼永不落幕”。普通卡牌使用不得触发该语音，取消/失败的技能事务也
  不得触发。再验证乌娜 `Skill2` 与哥伦比娅两个技能按各自一基序号绑定。
- 查看 AuraTools 语音配置，旧技能 `actionId` 应一次性迁移为 `skillSlot` 并清空；把只有
  一个主动技能的角色配置为序号 2 应失败关闭，不得退回卡牌 id 匹配。
- 在主机本机获取【星辰序曲·承】，确认 `Front/background` 的原生 Mesh gate 选择实际
  `Front/FrontBack.MeshRenderer` 并使用 `AuraTools/CardFrameEffectURP`；同节点遗留 Image
  的 sprite/material 必须保持原样。卡框呈现星尘效果且不出现紫色错误材质；此效果是本地
  工具表现，不产生卡面 RPC，也不要求客机同步启用。
- 连续执行【炽冕崩落/星辰序曲动态材质 → 卡牌退出并回池 → 晨星：星台主题框 → 再次
  获取动态卡】；每次重绑都必须先恢复原生材质，再销毁旧动态材质。主题卡只修改原生
  Mesh 路径，不得写入遗留 Image，也不得继承已销毁 shader；任何阶段都不能出现紫色矩形。
- 发布前重跑 VisualBundle 构建，日志必须同时包含 UI Image 与 WorldSpace Canvas
  MeshRenderer 两条 Direct3D11 像素 smoke pass，并包含 Mesh 材质恢复/重绑 lease smoke。

## 结算后日志分类

- Terrias/Aura 结算、伙伴清理和输入恢复不得产生 Error。若离开联机后仅出现
  `SupabaseUploadAuthService: Failed to verify Steam upload access`，其调用栈必须完全位于
  游戏原生 Steam/Supabase 上传鉴权链；该项记录为外部 HTTP/平台服务失败，不归因于
  Terrias 联机结算，也不得通过吞掉全局错误或改写原生上传服务来规避。
