# 内容 MOD 训练包与玩家适配器

## 权责边界

内容 MOD 是新增规则、定义、权威结算和训练语料的唯一所有者。它把可共享目录注册到 AuraShared；AuraTools 只查询 AuraShared 当前激活目录、校验副本并消费，不枚举其他 MOD 的安装目录或私有数据目录。

共享注册固定使用：

- `ModuleId = CombatAI`
- `FeatureId = ContentPackage`
- `ScopeType = Global`
- `ScopeId = all`
- `ParticipantKind = Content`
- `Resource.Kind = Directory`
- `Resource.ResourceId = package.json/PackageId`
- 注册 owner 必须等于 `package.json/OwnerModId`

AuraShared 将目录复制到规范共享存储并给出 `CanonicalPath`。AuraTools 只对 `QueryCatalog(... Visibility=Active)` 的结果调用 `ResolvePath`。

## 包目录

内容 MOD 自行维护源目录，注册时目录根必须包含 `package.json`：

```text
AuraCombatAI/
  package.json
  knowledge.json
  ruleset.json
  foundation-overlay.json
  transition-audit.json
  policy-adapter.json
  transformer-adapter.json
  training/
    authoritative-episodes-v5.jsonl
```

每个被引用文件都必须在该目录内，并在 `package.json` 中写入小写 64 位 SHA-256。绝对路径、`..` 越界、重复路径、摘要错误、owner/resourceId 不一致会使整个包失效。`GameBuild` 必须与运行时规范化版本精确一致；运行时版本不可取得时也按严格模式拒绝，不把未校验包降级加载。

## 应收集的数据

### 推理知识

`knowledge.json` 提供公开可推导的卡牌、技能、状态、敌人、遗物与祝福语义。`OwnerId` 必须等于内容 MOD owner。新增模型特征还要在 `PublicFeatures` 中声明 scope、数值类型、上下界和默认值；隐藏信息不得声明为公开特征。

`PublicFeatures` 是可观测特征的 allowlist 和数值契约，不是取值脚本。内容 MOD 仍须在自身权威观察、声明式 ruleset/overlay 或注册 Episode 中产出相同 key 的实际数值，并保证实机与模拟侧含义一致；AuraTools 不调用内容 MOD 私有回调来猜测这些值。未实际产出的声明特征不会凭空进入模型。运行时会按声明范围裁剪 number、把 boolean 规范为 `0/1`；内建精确 key 不得重定义，多个包对同一 scope/key 给出不同契约时，相关包全部拒绝。

### 权威底模训练

启用 `FoundationTrainingEnabled` 时必须同时提供：

- `ruleset.json`：新增卡牌、敌人、状态，所有定义的 `OwnerModId` 必须等于包 owner；ID 不得覆盖基础规则或其他已加载包。
- `foundation-overlay.json`：新增敌人池、遭遇、奖励、策略、难度、角色先验和构筑倾向。
- `transition-audit.json`：同一公开压缩状态与动作的完整状态对照、下一状态、结果，以及实机/模拟结算哈希。
- `training/*.jsonl`：可选的 `aura.combat-ai.episode.v5` 权威轨迹；必须标记 `Authoritative=true`、保持 campaign integrity，并通过帧/动作/有限数值校验。
- `DeclaredCoverage`：必须设为权威已知，并完整列出本 MOD 的卡、角色技能、敌人、状态、遗物和祝福 ID；卡/角色技能并集、敌人和状态集合必须与 ruleset 中本 owner 的实体精确相等。

转移审计至少有一个完整且 ID 唯一的 case，并且必须满足：相同压缩状态和动作不能因被省略的完整状态而产生不同下一压缩状态、下一完整状态、结果或结算；实机结算哈希必须等于模拟结算哈希。完整状态哈希应基于决策相关的规范状态生成，不包含纯表现字段。失败包可以提供知识，但不能进入底模合并与训练。

训练 Episode 由所在 package manifest 及其 SHA-256 授权。AuraTools 读取后会重新计算动作 owner，并在内存中绑定本次合并后的 `ContentSetHash`、`OwnerModSetHash` 与 `RulesetHash`，再随 schema 12 Worker 任务送入底模 replay。源文件不应预填最终 `ContentSetHash`，否则会形成“文件摘要参与集合哈希、文件内容又引用集合哈希”的循环。任一已声明训练文件为空、行损坏、owner 未注册或 Episode 不完整，整次底模训练都会拒绝启动。单文件上限 128 MiB，内容集合总上限 256 MiB/8192 条 Episode。`authoritativeContentReplayShare` 默认 `0.20`、可在高级训练设置中调到 `0..0.50`，用于保证注册语料不会被大规模自博弈 replay 完全挤出。

### 内容低秩适配器

`policy-adapter.json` 使用 `aura.combat-ai.adapter.v1`，类型为 `content-low-rank`。它必须绑定 owner、package 和明确的 `BaseModelId`，提供状态/动作低秩因子、rank 权重与有界策略 logit 修正。内容适配器可以由内容 MOD 的权威离线数据训练；不得代替规则集和转移审计。

### Transformer LoRA v2

`transformer-adapter.json` 是可选工件，使用 `aura.combat-ai.transformer-adapter.v2`。Manifest 必须绑定 owner/package、底模 ID 与 SHA-256、Tokenizer/规则 IR 版本、完整内容集合、训练数据和权重摘要。矩阵 rank 限制为 1 至 32，目标模块不得涉及合法性、权威规则、精确 Chance 或执行事务；个人偏好适配器只能修改 `actor.*`。

CPU 发布先按规范 Adapter ID 顺序预合并，再量化。缓存键固定为 `{baseHash, adapterHashes, backend, precision}`；内容不激活时不合并对应增量。当前代码已提供严格校验、内容感知激活、确定性组合和稠密权重预合并，在线 Transformer Runtime 晋级前该工件只参与打包与 Shadow 门禁。

## 内容集合身份

AuraTools 对激活包按 owner/package 排序，使用规范化后的依赖、覆盖、公开特征、foundation 开关、每个工件摘要、包版本和游戏版本计算；JSON 空白、属性顺序或列表书写顺序不会单独触发重训：

- `ContentSetHash`：当前规则/训练内容集合身份；
- `OwnerModSetHash`：参与内容 owner 集合身份。

底模包、检查点、在线样本、战斗 Episode、旅程 Episode 和玩家适配器都携带这两个值。内容专用底模只允许在完全相同的集合上激活；空内容集合底模视为通用底模，可由当前内容适配器做有界扩展。

## 玩家决策训练

玩家实战数据写入 AuraTools 自有共享数据目录，而不是内容 MOD 目录：

```text
AuraShared/Data/AuraToolsExp/AuraCombatAI/Datasets/Live/<ContentSetHash>/
  auto-battle-training-v7.jsonl
  live-combat-episodes-v5.jsonl
  journey-episodes-v1.jsonl
```

每条记录包含 `BaseModelId`、`ContentSetHash`、`OwnerModSetHash`、`ActiveAdapterIds`；候选动作包含归属 `OwnerModId` 和搜索回报分布。

本地训练冻结底模，只从“玩家真实选择不同于策略预选”的完成事务生成 `personal-residual`。当前实现是有界上下文线性残差，可视为低参数 rank-1 风格适配层。它只能调整决策/策略偏好，不能修改动作 Q、胜率、死亡概率或权威结算。导入时必须与当前底模和内容集合精确匹配，内容集合变化后自动停用并要求重新训练。

内容包的权威 Episode 只进入底模 replay，不直接训练玩家残差。内容 MOD 对玩家适配器的影响来自公开特征注册和玩家在该内容中的真实决策样本，这样不会把作者策略强行伪装成玩家偏好。

## 动作 Q

MLP v2 为每个动作输出 16 个回报分位数。训练目标来自 PUCT 边的经验回报分布：访问量至少 8，目标裁剪到 `[-1, 1]`，使用 Huber 分位损失。未访问边以 70% 均值和 30% 下尾均值组成 FPU；产生真实 rollout 后使用经验均值、风险和尾部统计。

这不是 DDPG：动作空间是离散、动态且带合法性约束，不需要连续动作 actor。也不在游戏进程执行在线 TD 更新；在线数据先持久化、按内容集合隔离，再离线训练和门禁验证，避免灾难性遗忘和权威语义漂移。

## 发布检查

1. AuraShared v4 manifest 能成功注册目录，catalog 显示 active/available/effective。
2. 包 identity、游戏版本、依赖、路径和全部 SHA-256 通过。
3. foundation 包通过转移一致性、状态混叠、owner 和 ID 冲突审计。
4. 底模、MLP 适配器、Transformer LoRA 和玩家适配器绑定正确的 base/content/owner identity，量化兼容性明确。
5. `Test-AuraCombatAi.ps1`、`Test-AuraFoundationTrainer.ps1` 与共享发布门禁全部通过。

参考文件：[内容包示例](examples/content-package/package.json) 与 [AuraShared 注册示例](examples/content-package.shared-manifest.example.json)。
