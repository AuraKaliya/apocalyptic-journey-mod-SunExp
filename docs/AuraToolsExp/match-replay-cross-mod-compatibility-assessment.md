# AuraToolsExp 对局回放：跨 MOD 兼容性评价

## 结论

AuraToolsExp v17 可以正确记录“进入原生可见战斗表面”的其它 MOD 内容，也可以通过 Aura 共享回放
协议记录额外状态和表现；它不能、也不应猜测任意 MOD 的私有 Unity 对象、隐藏状态或脚本语义。

因此兼容性不是“安装任意 MOD 后自动复制一切”，而是以下两层的组合：

1. 通用原生观察：`FightManager.statuses` 中的实体、HP、防御、BUFF、原生卡牌、牌区、技能、意图、
   `FightUI.CallActionAnimation`、效果、音频和实际 transform/material 轨迹；
2. owner-qualified 扩展：内容 MOD 通过 `IAuraReplayVisibleStateProvider`、
   `IAuraReplayEntityPresentationProvider`、`IAuraReplayPresentationModule` 声明原生表面之外的状态、
   布局和表现事件。

内容 MOD 可以使用普通紧凑 JSON 序列化，不需要手工控制属性声明顺序。共享表现边界会统一拒绝重复
字段/无效 JSON，并递归生成与 v17 文档一致的 canonical JSON；AuraToolsExp 最终验证器继续复验。

未注册的私有表现不会被 AuraToolsExp 反射扫描或复制。`ProviderRequired` 模块在播放时缺失对应 build/
renderer capability 会明确拒绝结构化播放；`Portable` 模块只使用 v17 通用实体、HUD、意图和 focus 原语。

## 能力矩阵

| 内容形态 | 自动记录 | MOD 需要接入 | 播放保证 |
| --- | --- | --- | --- |
| 原生 Role/Enemy/Partner/Status | 是 | 无 | 身份、代、阵营、HP、防御、BUFF、位置、动画资源 |
| 原生卡牌和 `FightUI` 动作 | 是 | 无 | 卡面/卡框/文本/费用、卡牌轨迹、角色/受击轨迹 |
| MOD 的额外可见状态 | 否 | visible-state provider | owner/type/schema/instance 的规范 JSON 增量 |
| MOD 的自定义实体布局/HUD | 否 | entity-presentation provider | 通用 WorldEntity 或 OwnerAttachedProxy 投影 |
| MOD 的额外表现事件 | 否 | presentation module | event time、event id、actor/owner/targets、persistent/transient |
| 私有 shader/复杂专属 UI | 否 | `ProviderRequired` renderer | 匹配模块存在时按手动回放时钟重建，否则拒绝 |

## Terrias 精灵

精灵属于“原生 Partner/Status + owner-attached 自定义布局 + 额外意图/聚焦表现”：

- 通用 recorder 保存实体状态、BUFF、动画资源和实际世界轨迹；
- `SpiritDeployment` 保存精灵养成/元素等可见扩展状态；
- Terrias entity provider 声明 owner-attached proxy、纵向血条和元素徽章；
- `SpiritBattlePresentation` 保存出退场、意图和动作 focus；
- 模块为 `ProviderRequired`，播放时需要匹配的 Terrias 表现能力。

2026-09-01 测试中的拒绝不是精灵声明缺失，而是 AuraToolsExp 将 late-bound 精灵事件写入严格单调的
全局时间轴，并修改已经追加的动作事件时间。v17 现已区分单调 Truth time 与可晚到的 Presentation
event time，并用 durability watermark 禁止持久化仍会变化的事件。

同日第二轮测试已经证明时间/因果修复生效，但暴露共享表现边界只 `Trim()`、未规范化多字段 JSON；
该责任现已收口到 `AuraReplayPresentationRuntime`，不再要求精灵、投影、Star Score 或其它内容 MOD
逐个按照属性名排序 payload。

第三轮长战斗暴露池化卡牌退出后对象仍存活：实际燃烧约 1.6 秒结束并回池，旧观察器却只等待 Destroy，
30 秒后写入 timeout diagnostic。当前录制器订阅共享 CardPresentation Reset，并以 visual root/source
精确关联；因此 Terrias 或其它 MOD 可以保留池对象，而无需让 AuraToolsExp 依赖其私有池实现。

第四轮录像已经正确进入 `Ready`，但播放预检把
`Terrias_terrias_enemycard_spirit_intent_adapter` 登记成 `Card`，随后把它的原生意图图标
`Icon/ActionIcon/给予异常` 当作必需卡面贴图，因而报告 `card-artwork` 缺失。该资源并未作为卡面缺失；
根因是隐式动作录制器曾无条件调用 `RegisterCard`。当前规则只读取原生 `IDataConfig.Type`：`Card`
进入卡牌目录，`EnemyCard` 与 `PartnerCard` 进入意图目录并产生 `Intent` 事务，其它类型使本次录制
明确失败。最终文档还会交叉验证事务类型与 descriptor 目录，防止同类错误再次封存为 `Ready`。

该结论适用于修复后新录制的战斗。已经封存的错误 v17 文档缺失意图底图等原始字段，且根哈希必须
保持不可变，不能在播放器中猜测或原地改写；它仍应被播放预检拒绝，需要重新录制。

## Terrias 投影

投影继承原生 `Partner`，Status 注册在 `FightManager.statuses`，动作通过
`FightActionPresentationApi -> FightUI.CallActionAnimation` 展示。因此实体、HP/BUFF、卡牌动作和实际
动画轨迹由通用 recorder 自动记录。

此前缺口是投影专属意图和生命周期没有共享回放声明。当前接入补充：

- `ProjectionDeployment`：role、owner、execution route、slot、generation、suspended 状态；
- portable `ProjectionBattlePresentation`：生成、退场和已解析意图图标/底图/数值/目标；
- 投影动作继续只采用原生动画观测，不再叠加第二套合成 focus 动画。

因此在 Terrias 当前实现下，精灵和投影都具备完整的记录路径；Star Score HUD 与 Wuna orbit fire 仍通过
各自的 `ProviderRequired` renderer 重建。

## 验收边界

自动门禁证明协议、事件时间、耐久前缀、模块注册、文档验证和产品编译。Unity 自动化之外仍必须用新
日志完成四组实机锚点：精灵召唤/意图/行动/退场、投影召唤/意图/卡牌行动/退场、拖动定位与倍速、
导出 MP4。成功标准是记录状态为 `Ready`，无 capture diagnostics，且实战与回放在相同语义时刻显示
相同实体、HP/BUFF、意图、卡牌和额外表现。
