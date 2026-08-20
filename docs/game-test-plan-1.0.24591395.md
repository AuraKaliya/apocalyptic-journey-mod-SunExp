# 1.0.24591395 游戏集成测试清单

本清单覆盖 AuraToolsExp、Terrias 与游戏 `1.0.24591395` 的高风险交界。自动化门禁负责结构、序列化和权限契约；以下项目仍需在真实主客机与战斗演出环境中确认。

## 战斗异常重建

分别测试“玩家断线触发重建”和“联机投票重开”。主机、客机各观察一次。

- 原战斗只触发一次 `FightRestarting`，不产生胜利、失败或逃跑结算。
- 新战斗进入完整的 `FightInitializing -> FightInitialized -> FightOpening -> FightStarted -> FightRestarted`；`FightStarted` 只由 `Fight_Start.Init` 提交一次，战斗 session 只递增一次。
- 日志出现成对的 `[BattleRestart] restarting` 与 `[BattleRestart] restarted`，其中 rebuilt session 大于 interrupted session。
- AuraToolsExp 自动战斗停止旧决策、清除待执行操作，并按设置在新战斗重新启用。
- 伤害统计清空旧战斗捕获，但不把旧战斗归档成胜负结果。
- Terrias 的投影、精灵、意图图标、选人窗口、场地、元素挑战与技能 CG 不残留旧对象。
- 重建后重新获得的投影/精灵只行动一次，不继承旧回合去重 token。
- 连续完成两场战斗并重开一次，确认【炎轮再临】、晨星祝福、【无刻时钟】等持久 DataConfig 的监听每场各触发一次，不因旧 Vars 标记而失效或重复。

## 卡牌动作事务与增量刷新

- 分别使用普通牌、攻击牌、回收牌和会嵌套使用其他牌的效果，确认每次动作只产生一组 `Attempting -> NativeStarted -> Committed -> PresentationCommitted -> Completed`。
- 制造一次脚本异常或不可用出牌，确认事务进入 `Aborted`，下一张牌不会继承白曜、星谱、黄金梦或深渊凝视的 pending 状态。
- 仅装备 Terrias Buff 且没有 `ReducePerUse` 变化时连续出牌，确认不再由 `CheckAllBuff` 触发 `FightUI.UpdateCardMsg` 全量刷新。
- 改变燃烧、日耀、聚焰、星辉和伪金，确认只刷新依赖卡牌；手牌包含未知第三方卡牌或变化来自未知 Buff 时保留原生全量刷新。
- 关闭性能诊断启动时不注册高频诊断 handler；运行中开启后能采样，关闭后订阅归零并提示重启才能移除已安装的宿主 dispatcher。
- Projection 主机不响应时按有界重试提示，30 秒后终止并返还卡牌；无 pending transaction 时场景中不存在 Projection NetworkRunner。

## Partner 与 Terrias 行动队列

使用至少一名官方 `Partner: OtherObj`，再分别加入 Terrias 投影、精灵，以及两者同时存在的组合。

- 官方 Partner 数量上限、状态栏与目标选择不受投影/精灵数量影响。
- 官方 Partner 保持原生行动位；Terrias 投影/精灵在敌方行动后进入独立的回合末同伴阶段。
- 队列中只存在一个 Terrias anchor；投影和精灵本体不作为原生 Partner 插入队列。
- 同一回合内，每个投影/精灵最多行动一次；重复刷新、受击刷新与状态同步不会追加一次行动。
- 在 Terrias anchor 行动前召唤的同伴可在当回合行动；anchor 行动后召唤的同伴等待下一回合。
- 回合中新增官方 Partner 时，仍遵循官方快照规则，不被 Terrias anchor 提前驱动。
- 观察日志中不应出现 `ProjectionTurnCoordinator.QueueInvariantViolation`。

## 联机事件与权限

- 主机与客机分别触发卡牌创建、弃牌、伤害、加 Buff 事件，确认 `ActionData`、`BurnData`、`CreateData`、`DamageData`、`AddBuffData` 的 id、类型与来源/目标一致。
- 至少测试一张原生配置和一张带 `RawData` 的动态配置；客机创建出的卡面、费用、脚本和 Buff 应与主机一致。
- 客机完成日耀回忆整备后，界面先显示“等待主机确认”，收到接受回执后才关闭。
- 主机确认后，主机 `RoleTables` 与客机本地角色的卡组、祝福、原点分配和 commit token 一致。
- 在等待回执时断开客机，确认窗口不会误报完成；重连后可以重新提交，不接受旧 token 或其他玩家的回执。
- 两名客机同时提交时，各自只消费属于自己 player id 与 token 的回执。

## AuraDirector

- 使用当前 `Witch.dll` 哈希 `88613CF3E1F0F4A493FE722FBFB63E36A6C97CBF098F9F406F6AC2A28C136F60` 启动，确认 provider 报告 `detour-compatible` 与游戏版本 `1.0.24591395`。
- 触发一次带 CG 的开战，原生 `ReadyToStart` 在 hold 释放后只执行一次。
- 关闭或卸载 provider 后再次开战，原生流程直接执行且不残留 Harmony prefix。
- 用非白名单 DLL 做隔离验证时，provider 应 fail-close 为 unsupported，但战斗启动流程应 fail-open，不得卡住开战。
