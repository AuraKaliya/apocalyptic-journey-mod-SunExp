# Aura 自动战斗共享协议 v1

> 后续开发、异机测试、模型训练和发布验收请从
> [Aura 自动决策与自动战斗文档](AuraCombatAI/README.md) 开始。

## 分层

- `AuraDecisionShared`：不依赖游戏对象的决策图、效用向量、候选排序、多选规划和残差模型接口。
- `AuraCombatAiShared`：战斗观察、候选动作、目标、交互请求、合法性规则、语义提供器和训练样本协议。
- `AuraCombatAiShared/GameApi`：Witch 的 `FightUI`、`DeckUI`、卡牌、技能和目标适配器。
- `AuraToolsExp-Dev/Features/AutoBattle`：设置、战斗按钮、单动作事务控制和训练样本落盘。
- 内容 MOD：只注册自己拥有的动作语义、合法性规则和复杂交互提示，不依赖 AuraToolsExp。

## 执行约束

1. 候选展开和评分不得调用 `CommonCardItem.TryUse`。该方法会移除手牌、扣费并触发事件，不是纯检查。
2. 每次只提交一个根动作；动作后等待动画、建牌队列和交互请求稳定，再重新观察和决策。
3. 定向卡牌和技能只在最终提交时写入目标并调用一次原生 `TrueUse`。
4. `DeckUI` 和 `FightUI` 选择通过原生按钮或原生选择入口完成，不直接调用业务回调。
5. 自动战斗关闭时不强制关闭已经出现的必选界面，而是把该界面交还玩家。
6. 未识别的自定义 UI 不做屏幕坐标点击；根据配置降权、尝试或交还玩家。

## 决策模型

默认模型使用以下效用维度：

- 生存、斩杀、节奏、资源、牌组经济、成长、联动、续航、风险、不确定性、协同。

`DecisionGraph` 可以用特征条件连接节点，并在节点上增加效用或拒绝候选。规则图先执行，随后进行加权效用排序，最后预留 `IDecisionResidualModel` 作为学习模型残差。首版不引入神经网络运行库，也不携带模型文件。

## 训练边界

开启“记录训练样本”后，AuraToolsExp 会将稳定动作前后的特征、预测分数和短期奖励写入：

`Logs/Owners/AuraToolsExp/auto-battle-training-v1.jsonl`

样本协议为 `aura.combat-ai.sample.v1`。训练应在游戏外进行；后续只加载经过协议版本、特征版本、大小上限和完整性校验的小型模型。运行时推理通过 `IDecisionResidualModel` 接入，不替换硬合法性规则。

## 内容 MOD 接入

- 使用 `CombatAiRegistry.RegisterSemanticProvider` 描述卡牌或技能效果。
- 使用 `CombatAiRegistry.RegisterPreflightRule` 提供纯、无 UI、无状态修改的合法性规则。
- 在原生选牌调用前使用 `CombatInteractionBroker.SetNextHint` 标记用途、区域、必选性和高/低价值偏好。
- 友方 AI 复用 `ICombatObservationProvider`、`CombatDecisionEngine` 和 `ICombatActionExecutor`，但应由友方单位自己的观察适配器和权限边界提供状态与执行。

## v1 支持范围

- 普通卡、定向攻击卡、无目标技能、目标技能、结束回合。
- `DeckUI.CreateDeckMenuForSelect` 单选/多选。
- `FightUI.SelectCardToAction` 手牌选择与确认。
- Terrias 焚毁选牌、洛奈尔指引、晨星换位语义。
- 随机结果后重新观察，不预测随机分支内部状态。

排序、滑条、拖放拼装、自由文本和未知自定义窗口暂不自动处理。
