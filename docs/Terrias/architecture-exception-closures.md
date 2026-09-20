# Terrias 分层迁移与例外关闭

2026-09-06 整改将例外预算从 64 收紧至 49，移除经语义检查确认已不再使用的 15 条许可。2026-09-07 联机整改又将日耀提交、余烬同步与手部附着用例移入 Application，数据放入 Contracts，传输通过注入回调连接，关闭 6 条对应许可，预算收紧至 43。其余 43 条是已有能力的独立迁移，仍是技术债，不能表述为全库分层已完成。

2026-09-20：日耀地图预览策略已移至 Application，GameApi 只保留宿主访问与原生资源能力检查，关闭 SolarMemoryMapPreviewApi → Mechanics 例外，当前剩余预算为 42。其余准备、地图流程等既有例外仍按原负责边界处理。

投影召唤由 `Application/ProjectionSummonService` 负责应用事务，`Mechanics/ProjectionLifecycle` 定义规则层的生命周期出口，组合根绑定唯一实现。传输只通过 `IProjectionNetworkPort`，数据通过 Contracts。原生 `RpcCommandBaseSerializer` 使用 `TypeNameHandling.All`，所以 `Network/ProjectionSnapshotWire` 保留既有完整类型名；应用 DTO 不直接上网。当前 Partner 协议为 24，接入共享联网战斗身份；卡组模型仍为 v3，转换必须保持全部字段。

Buff 的正面效果排除规则归 `TerriasBuffClassificationPolicy`；GameApi 通过注入策略与余烬持久化回调调用所属能力。共享基础失败或绑定未完成时不激活依赖它们的玩法。不允许新增反向引用替代绑定。

| 剩余能力负责人 | 关闭条件 | 对应验证 |
|---|---|---|
| SpiritCollection | 捕获/召回/收藏与成长用例归 Application，GameApi 仅操作宿主，RPC 调用应用入口 | 精灵收藏、捕获、召回与成长行为；发送者绑定；生产编译 |
| SolarMemory | 准备、地图预览和角色提交的状态归应用用例，UI/网络只投递输入与显示结果 | SolarMemory 流程、事件/地图、角色提交与联机权威 |
| ProjectionAndControl | 余下心变/动作表现入口采用应用端口，心变状态不通过 Network 类型进入规则层 | Partner 回合/座位/退款、原生传输类型、权威与生命周期 |
| AdventureModes | 海域/深渊结算、撤离和地图适配的数据与用例分离 | 幂等结算、流程、地图与事件检查 |
| ElementalCombat | 元素/场地/命座/余烬的提交事件由 Application 驱动 Network | 元素规则、场地同步、命座、冒险持久化 |
| CombatPresentation | 卡牌索引和展示失效通知使用规则层接口，Hooks 负责表现与订阅 | 卡牌失效、真实出牌生命周期、生产编译 |

每条具体来源与目标层见 `tools/architecture-boundary-exceptions.json`。每批完成后必须移除对应许可并降低 `maxExceptions`；不能单改标签、目录或提高预算来通过门禁。当前门禁仍检查所有生产文件、别名与完整限定类型，禁止新增跨层环。
