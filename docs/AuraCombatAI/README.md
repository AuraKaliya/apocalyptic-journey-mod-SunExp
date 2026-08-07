# 自动战斗 AI 当前方案

这里是自动战斗 AI 的唯一权威文档集。01 至 10 号文档描述当前生产合同；11 号文档同时记录下一代目标架构和阶段落地状态。对象 IR、类型化动作、Shadow Token、6 层 Transformer 世界模型教师、通用治理、模型调用预算和 Transformer LoRA v2 已进入代码；游戏内主动决策仍以 MLP + 权威前向模型 PUCT 为 Champion。除显式声明兼容的 v3 正式底模包外，当前实现不提供旧数据读取、旧配置转换或旧搜索器回退。

## 文档导航

1. [系统架构](01-系统架构.md)：模块边界、数据流与运行模式。
2. [观测与执行安全](02-观测与执行安全.md)：玩家等价信息边界、合法性和事务执行。
3. [风险敏感根抽样 PUCT](03-风险敏感根抽样PUCT.md)：搜索、风险统计、排序、早停和预算。
4. [知识与语义扩展](04-知识与语义扩展.md)：权威知识、动作语义、前向模型和 MOD 扩展。
5. [训练与模型门禁](05-训练与模型门禁.md)：样本、轨迹、特征、AdamW、切分和模型晋级。
6. [权威模拟与底模训练](06-权威模拟与底模训练.md)：无 UI 模拟、课程学习、Worker 和检查点。
7. [测试与发布验收](07-测试与发布验收.md)：自动门禁、实机验证和故障处置。
8. [情景旅程评估](08-情景旅程评估.md)：同种子对照、普通/高级难度与结果解释。
9. [训练与游戏主体验证分离](09-训练与游戏主体验证分离.md)：外部规模训练、隐藏实机验证与晋升回执。
10. [内容 MOD 训练包与玩家适配器](10-内容MOD训练包与玩家适配器.md)：AuraShared 注册、内容集合、转移审计、数据目录和残差适配器。
11. [Transformer 世界模型与分层决策目标架构](11-Transformer世界模型与分层决策目标架构.md)：对象状态、类型化动作、双世界模型、Chance-PUCT、Actor、Governance、6 层模型与 LoRA v2。

11 号文档中标记为 Active 的能力属于生产合同；标记为 Shadow/Training 的能力可以产出诊断或训练工件，但不取得在线动作控制权。尚未通过相同硬件预算门禁的 latent Chance-PUCT 和 Actor 快速路径不得宣传为生产能力。

## 当前合同

| 领域 | 当前值 |
|---|---|
| 在线决策 | `risk-aware-root-sampling-puct-mpc` |
| 搜索预算 | `dynamic`，质量档 `fast / balanced / deep` |
| 在线样本 | `aura.combat-ai.sample.v7`，特征 10 |
| 选择轨迹 | `aura.combat-ai.selection.v1` |
| 长期轨迹 | `aura.combat-ai.episode.v5`，特征 26；附带对象 Observation Envelope |
| 策略价值编码 | `partitioned-v3` |
| 策略价值模型 | `aura.combat-policy-value.mlp.v2`，16 分位动作 Q |
| 内容包 | `aura.combat-ai.content-package.v1` |
| 适配器 | `aura.combat-ai.adapter.v1` |
| Transformer 对象协议 | `aura.combat-world-model.observation.v1` / action v1 / transition v1，Shadow |
| Transformer 教师 | `aura.combat-transformer-world-model.v2`，6 层、384 hidden、8 heads、1536 FFN；1024 frames 启动、累计语料、分层固定锚点、跨轮热启动与成熟度蒸馏，Training |
| Transformer 运行时 | `aura.transformer-runtime-probe.v1`，自动发现/验证 Python、PyTorch、NumPy 与 CPU/CUDA |
| Transformer 适配器 | `aura.combat-ai.transformer-adapter.v2`，可选内容工件，未取得在线控制权 |
| 在线治理 | 墙钟截止、模型调用预算、风险偏好、安全回退；Actor 裁剪默认关闭 |
| Worker | schema 12 |
| 训练治理 | `foundation-governance-v26-productive-progress-pareto-arena` + `foundation-stagnation-v2-productive-progress` + `paired-evidence-v5-noninferiority` |
| 自动并发规划 | `foundation-parallelism-v2-adaptive-exact-capacity` + `foundation-auto-tune-v11-adaptive-exact-capacity`；控制台不再暴露 CPU 并行度，按逻辑处理器数生成 50%/75%/100% 实测点，以稳定吞吐自动选取并跨迭代保持；内存预测仅作为安全上限，不再把并发向下取整到固定档位，模型训练并行度自动使用可用逻辑处理器 |
| 外部模型包 | 写入 `foundation-model-package-v4`；读取兼容正式验收的 v3 |
| CLI 搜索策略 | `risk-puct` |

输入不满足这些合同会被拒绝。需要改变布局时，应同时切换写入、读取、测试、示例与文档，并删除被替代内容。
