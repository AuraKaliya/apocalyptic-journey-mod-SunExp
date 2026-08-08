# 自动战斗 AI 当前方案

这里是自动战斗 AI 的唯一权威文档集。01 至 10 号文档描述当前生产合同；11 号文档同时记录下一代目标架构和阶段落地状态；12 号文档记录内容 MOD Rule IR 训练合同与导出认证的下一版设计基线；13 号文档给出独立底模训练器的端到端数据流、存储协议、优化优先级与重训清单。对象 IR、类型化动作、Shadow Token、6 层 Transformer 世界模型教师、通用治理、模型调用预算和 Transformer LoRA v2 已进入代码；游戏内主动决策仍以 MLP + 权威前向模型 PUCT 为 Champion。正式模型包兼容范围以模型包协议为准；训练恢复层只保留显式声明的 v11 checkpoint / JSONL 与 Replay Warehouse v1 路径，fresh/reset 会在同一严格目录边界内同时清除 v12 与 v11 主备工件，避免旧训练状态复活。Replay v1 仅在 v2 shard、事务索引和回读验证全部成功后做可恢复批迁移并压缩旧索引；compact-only 数据缺少持久化 Token Catalog 时拒绝迁移，不做任意旧数据或旧搜索器的猜测式转换。

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
12. [MOD 底模训练 Rule IR 合同与导出认证](12-MOD底模训练Rule-IR合同与导出认证.md)：实机 C# 与训练 Rule IR 双轨权威、两阶段导出、差分认证、动态内容组合和作者 Skill。
13. [独立底模训练器数据流与优化优先级](13-独立底模训练器数据流与优化优先级.md)：阶段输入输出、稀疏特征、Replay/checkpoint 协议、P0–P3 路线、成熟框架评估和重新训练前检查清单。

11 号文档中标记为 Active 的能力属于生产合同；标记为 Shadow/Training 的能力可以产出诊断或训练工件，但不取得在线动作控制权。12 号文档是待实现设计，不把当前 `content-package.v1` 自动提升为已认证 v2 内容。尚未通过相同硬件预算门禁的 latent Chance-PUCT 和 Actor 快速路径不得宣传为生产能力。

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
| Transformer 教师 | `aura.combat-transformer-world-model.v2`，6 层、384 hidden、8 heads、1536 FFN；1024 frames 启动、稀疏累计语料 generation 事务、有界增量+回放、resident/512-frame 大分片、固定锚点、仅可信代跨轮热启动与成熟度蒸馏；signed Seed 只在 NumPy legacy 边界映射 uint32，checkpoint RunSeed 控制恢复随机流，确定性配置/协议/执行边界故障会保存断点并阻断正式底模，Training |
| Transformer 运行时 | `aura.transformer-runtime-probe.v1`，自动发现/验证 Python、PyTorch、NumPy 与 CPU/CUDA |
| Transformer 适配器 | `aura.combat-ai.transformer-adapter.v2`，可选内容工件，未取得在线控制权 |
| 在线治理 | 墙钟截止、模型调用预算、风险偏好、安全回退；Actor 裁剪默认关闭 |
| Worker | schema 12 |
| 训练持久化 | `AURAFES5` 有界二进制快照；Replay v2 self-contained shard Token Catalog + checksummed 事务索引，v1 只做提交/回读验证后的可恢复迁移；checkpoint catalog 使用 generation/checksum 与 artifact hash，GC 取有效 primary + backup 两代可达集并保留 active `.bak` 的快照，uncertain 状态一律禁止破坏性 GC；reset 以 durable marker 先使恢复指针失效再删除工件；当前 Worker 存储/恢复与教师安全聚合门禁基线为 69 条断言 |
| 训练治理 | `foundation-governance-v26-productive-progress-pareto-arena` + `foundation-stagnation-v3-behavior-vs-pipeline-progress` + `paired-evidence-v6-absolute-qualified-best` |
| 自动并发规划 | `foundation-parallelism-v4-phase-aware-128m-reserve` + `foundation-auto-tune-v12-signed-microbenchmark`；推理计划按硬件、模型协议/特征版本、张量形状和并发档位签名复用，仅在真实 Replay 输入上运行有界微基准；运行期健康失败切换 direct 并进入持久化冷却。常规阶段固定保留 128 MiB，Transformer 阶段按预测峰值另保留 128 MiB；每轮由独立 Worker 子进程执行并在轮末退出，跨进程 Replay 检查点最多携带 512 Episodes / 48000 Frames / 256 MiB 估算常驻量，其余进入压缩磁盘仓库 |
| 外部模型包 | 写入 `foundation-model-package-v5.json` + `foundation-model-weights-v5.bin`；读取兼容正式验收的 v3/v4 JSON 包 |
| 内置底模发布 | `ModResource/Model/<角色名 [RoleId]>/<发布名 [PackageSha 前 12 位]>/` 两层隔离同名 canonical pair；启动时批量校验包、同目录权重和精确 ArtifactSha 信任后注册，但不自动选择或启用 |
| CLI 搜索策略 | `risk-puct` |

输入不满足这些合同会被拒绝。需要改变布局时，应同时切换写入、读取、测试、示例与文档，并删除被替代内容。
