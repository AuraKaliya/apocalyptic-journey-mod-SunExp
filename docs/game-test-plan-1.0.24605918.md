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

- 主客机同时拥有 Projection turn anchor；其 status ID、队列位置和
  `ObjectAction.allCards` 顺序一致。只有主机执行同伴逻辑，客户端只消费原生意图。
- Projection 在整场战斗中保持完整、稳定排序的卡牌目录，只用冷却选择本回合卡；
  Spirit 保持同一个行动牌槽位，不通过替换 `FightAction` 改变目录。
- 同轮创建 Projection/Spirit 后，主机发送意图前客户端必须已建立相同卡牌 ID；
  不得出现 revision 已消费但解析为空，或 pending batch 永久等待。
- HeartChange 保持原生 Enemy 作为 `ActionQueue` 与批量意图身份，proxy 只在
  before-action hook 执行。主机下发的 intent count 在客机复用，不能由客机竞争计算。
- 重连、重开战斗和连续两次 HeartChange 后，不得残留旧 anchor、proxy、statusData
  或重复行动；协议版本不匹配时必须拒绝并记录日志。

## 主客机回归

- 分别覆盖主机一人、主机加一名客户端、两名客户端三个场景。
- 每轮记录 `ActionQueue` status ID、revision、card ID 和 allCards 哈希，确认所有节点一致。
- 覆盖官方 Partner、Projection、Spirit、HeartChange 两两组合以及三者同时存在。
- 断线重连后重新建立目录与 revision；不得接受旧战斗或其他玩家的意图。
