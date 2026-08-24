# 测试集治理与调整计划

日期：2026-08-07

## 范围与结论

本轮检查覆盖：

- `tools/Test-*.ps1`：35 个入口，共 2,818 行非空脚本。
- `*Tests` C# 项目：15 个、53 个 C# 文件，共 29,348 行非空测试代码。
- `tools/shared-release-matrix.json`：21 个正式共享发布步骤。
- 共享层、AuraSharedCore、AuraToolsExp、Terrias，以及隔离后的 TestMods。

原主要问题不是单纯的测试数量，而是测试所有权、调用层级和断言形态混杂。
P0-P3 及补充开发现已全部完成；当前测试体系已从隐式全套调用和源码快照，
转换为按 owner、profile、impact tag 选择的行为、内容、工件、架构和发布验证：

- 重复执行 Core 的旧共享入口已删除，矩阵步骤不再通过子脚本隐式重复调用。
- Terrias 与共享架构入口只保留声明式系统边界，不保存功能算法或私有布局快照。
- Tool、内容、行为、训练工件、worker、发布维护已使用独立入口和 profile。
- Terrias 角色/功能专项由矩阵显式选择，不再由 CSharp 或 Architecture 总入口反向调用。
- 正式产品范围内已审计的旧源码字符串约束已迁为行为、结构化数据或通用边界验证。

## 已完成的基线调整

- `TestMods` 已隔离到 `tools/Test-TestMods.ps1`。
- `Build-SharedRuntimeConsumers.ps1` 不再提供构建 TestMods 的路径。
- TestMods 不进入 shared release matrix，不属于产品消费者或发布制品。
- `Test-SkinExp.ps1` 从 439 行缩减为仅验证 SkinExp 原型自身的 48 行入口。
- 新增 `AuraSkinShared.Tests`，行为验证 owner-qualified 身份、跨 owner 候选、
  优先级、候选启停、默认恢复、选择重映射、资源路径边界、安装前置失败和
  协议兼容范围。
- Terrias 和 AuraToolsExp 已分别接管自己拥有的皮肤内容验证。
- skill 已改为按影响选择验证，并加入测试退休规则。
- 共享发布矩阵已升级到 schema v2，每个步骤声明 owner、category、cost、
  impactTags 和 profiles，并支持 `-Profile`、`-Tag`、`-StepId`、`-List`；未给
  selector 时拒绝运行，不再隐式选择 `full-release`。
- 新增 `tools/terrias-test-matrix.json` 与 `tools/Test-TerriasGate.ps1`；默认不
  猜测验证范围，必须显式选择 profile、tag 或 step。
- 已删除 `Test-MainSharedFramework.ps1`；Architecture 不再调用 Spirit；
  Terrias CSharp 不再调用 Columbina 或 Elemental。
- `Test-TerriasCSharp.ps1` 的临时 here-string harness 已迁入正式
  `Terrias-Dev.Tests` 项目；PowerShell 入口缩减为构建和行为测试编排。
- `Test-TerriasArchitecture.ps1` 与 `Test-SharedArchitectureGuidelines.ps1`
  已改用 `architecture-boundary-rules.json`，只验证命名空间、依赖方向、
  Hook 隔离、资源/配置入口和 CSV managed entry 边界。
- Core 行为、生产共享 DLL 构建已拆为 `Test-AuraSharedCore.ps1` 与
  `Build-AuraSharedRuntime.ps1`；单领域 profile 会显式包含共享构建。
- compatibility baseline 已删除 7 组、104 个私有源码 snippet，只保留
  4,951 项反射公共 API。
- AuraToolsExp 与 Combat AI 已拆成 Tool 行为、知识、训练工件、仿真验收、
  worker 集成和发布归档入口；普通 Tool/共享改动不再触发 Foundation worker。
- `AuraCombatAiShared.Tests` 已拆为决策、仿真、策略价值、战役、Foundation
  训练、协议工件和夹具文件；`AuraToolsExp.NativeReward.Tests` 已拆为角色、
  内容、运行时和夹具文件，命令入口保持不变。
- `AuraSharedCore.Tests`、`AuraToolsExp-Dev.Tests` 与
  `AudioArbiterShared.Tests` 的单体 `Program.cs` 已按行为域和夹具拆分。
- Spirit 已拆为内容、Registry、Runtime 三个显式矩阵步骤；Columbina 和
  Elemental 的源码实现锚点已迁到所属 C# 行为项目或结构化内容验证。
- 网络 profile 现在显式运行 Core、CG、Audio 行为测试与通用 RPC 扫描；CG
  sender 绑定已统一到 `AuraRpcAuthorityRuntime`，scanner 按同一注册块校验
  marker predicate 和 `BindServerSender`。
- 共享架构、共享写入口、内容/工具边界统一读取
  `architecture-boundary-rules.json`，不再各自保存重复源码断言。

## PowerShell 测试入口审计

| 入口 | 当前性质 | 调整决定 |
| --- | --- | --- |
| `Test-AudioArbiterShared.ps1` | 16 行行为项目包装器 | 保留；作为共享域行为入口模板 |
| `Test-AuraCgShared.ps1` | 16 行行为项目包装器 | 保留；作为共享域行为入口模板 |
| `Test-AuraSkinShared.ps1` | 15 行行为项目包装器 | 保留；覆盖共享皮肤选择、路径、安装前置失败和协议兼容 |
| `Test-AuraCombatAi.ps1` | 19 行行为项目包装器 | 已拆出知识、训练工件、仿真验收和发布归档入口 |
| `Test-AuraCombatKnowledge.ps1` | 知识库编译与数据验证 | 保留为 AI 数据专项，不由普通共享域改动触发 |
| `Test-AuraFoundationArchiveMaintenance.ps1` | 训练归档维护 | 从 AI 默认行为测试中解耦，仅归档或发布任务运行 |
| `Test-AuraFoundationTrainer.ps1` | 948 行外部 worker 集成烟雾测试 | 保留，但只属于训练 worker/发布 profile |
| `Test-AuraDirectorDetour.ps1` | 42 行行为与发布工件包装器 | 已退休私有源码锚点；保留行为与二进制所有权验证 |
| `Test-AuraNativeRewards.ps1` | 25 行行为项目包装器 | 保留 |
| `Test-AuraSharedCore.ps1` | 14 行正式行为项目包装器 | 已与 `Build-AuraSharedRuntime.ps1` 分离，结构快照已退休 |
| `Test-AuraToolsExp.ps1` | 93 行 Tool 行为与结构化 Tool 自有内容验证 | 已移除运行时源码快照，保留当前配置和共享声明契约 |
| `Test-AuraCombatTrainingArtifacts.ps1` | 训练器自测、预编译程序清单和底模包验证 | 独立工件入口；不运行 worker |
| `Test-AuraCombatSimulationAcceptance.ps1` | Headless CLI 固定输入输出验收 | 独立仿真验收入口 |
| `Test-ContentToolSharedBoundary.ps1` | 9 行声明式规则包装器 | 与共享架构共用规则文件，独占内容/工具依赖边界 |
| `Test-MainSharedFramework.ps1` | 已删除 | 消费者构建由 `Build-MainSharedConsumers.ps1` 直接承担 |
| `Test-NetworkRpcAuthority.ps1` | 146 行通用 RPC 安全扫描 | 检查 payload 身份授权、裸 transport，以及同一注册块内的 server-bound marker predicate/sender 绑定 |
| `Test-SharedArchitectureGuidelines.ps1` | 9 行声明式规则包装器 | 已只保留共享产品独立性与 Core 依赖方向 |
| `Test-SharedDllPackaging.ps1` | 55 行项目引用和 DLL 哈希验证 | 保留为唯一 DLL 分发权威门禁 |
| `Test-SharedReleaseGate.ps1` | 矩阵编排器 | 保留；必须显式选择 profile/tag/step，完整发布能力由 `-Profile full-release` 保留 |
| `Test-SharedRuntimeCompatibility.ps1` | 58 行公共 API compatibility 包装器 | 只保留反射公共 API baseline；源码 snippet 已删除 |
| `Test-SharedWriteEntrypoints.ps1` | 9 行声明式规则包装器 | 与共享架构共用规则文件，独占共享写入口边界 |
| `Test-FamiliarGrowth.ps1` | Familiar 行为项目包装器，无默认调用方 | 保留为 Terrias 功能专项，由影响矩阵显式选择 |
| `Test-SpiritCapture.ps1` | 115 行结构化内容验证 | 已从 Architecture 解耦；只拥有卡牌、意图、捕获配置和资源内容契约 |
| `Test-SpiritRegistry.ps1` | 15 行 Registry 行为项目包装器 | Spirit profile 的独立 schema/registry 步骤 |
| `Test-SpiritRuntime.ps1` | 14 行 Runtime 行为项目包装器 | Spirit profile 的独立概率、冷却、效果、身份和生命周期步骤 |
| `Test-TerriasArchitecture.ps1` | 48 行边界验证入口 | 已收缩为命名空间、依赖、Hook、CSV managed entry 和共享引用边界 |
| `Test-TerriasBranding.ps1` | 品牌、发布面和目录内容检查 | 保留为内容/发布专项，不属于普通 C# 行为测试 |
| `Test-TerriasColumbina.ps1` | 142 行角色内容、资源、共享声明和行为项目编排 | 源码实现锚点已退休；内容与 17 条角色行为断言各自归属明确 |
| `Test-TerriasCSharp.ps1` | 35 行正式测试项目包装器 | harness 已迁入 `Terrias-Dev.Tests`，实现快照已退休 |
| `Test-TerriasElemental.ps1` | 56 行 Elemental 行为与结构化 Buff 数据验证 | 已从 CSharp 总入口解耦并清理运行时/RPC 源码锚点 |
| `Test-TerriasResources.ps1` | 结构化资源、注册表和路径验证 | 保留；继续复用通用资源包验证器 |
| `Test-GameManagedDecompile.ps1` | Managed 检查工具自测 | 保留为开发工具专项，不进入产品或共享发布门禁 |
| `Test-TestMods.ps1` | TestMods 唯一编排入口 | 保留；只有显式原型维护任务运行 |
| `Test-SkinExp.ps1` | SkinExp 原型接入和内容验证 | 保留在 TestMods 内，不再承载共享或产品语义 |
| `Test-GoldExpCSharp.ps1` | GoldExp 历史源码快照 | 仅归档在 TestMods；不投入产品测试重构预算 |

## C# 测试项目审计

| 项目 | 规模 | 调整决定 |
| --- | ---: | --- |
| `AudioArbiterShared.Tests` | 8 个文件，`Program.cs` 保持薄入口 | 已按 manifest/file policy、request/network、provider、技能序号、coordination 和夹具拆分，当前 475 条断言 |
| `AuraCgShared.Tests` | 839 行 | 保留；当前规模可接受 |
| `AuraSkinShared.Tests` | 2 个文件，326 行，36 条断言 | 覆盖安装前置失败、路径逃逸、协议范围和可释放运行时皮肤作用域 |
| `AuraSharedCore.Tests` | 11 个文件，`Program.cs` 407 行 | 已按 game data、资源协议、领域、存储恢复、生命周期和夹具拆分；1,243 条断言覆盖终局生产关闭与 Finalized 屏障 |
| `AuraSharedCompatibility.Tests` | 366 行 | 保留为公共 API 兼容权威测试 |
| `AuraDirectorDetour.Tests` | 241 行 | 保留；PowerShell 私有源码锚点已退休，当前验证 hold/re-entry、fail-open、指纹 gate 和安装所有权 |
| `AuraCombatAiShared.Tests` | 10 个领域文件，`Program.cs` 15 行 | 已按决策、仿真、策略价值、战役、Foundation、协议工件和夹具拆分 |
| `AuraToolsExp-Dev.Tests` | 32 个文件，`Program.cs` 80 行 | 已按配置、共享功能、卡牌视觉原生 Renderer 契约与材质租约、对局回放/媒体/数据库、伤害统计、历史、安全箱/初始牌组和夹具拆分；当前 1,340 条行为断言 |
| `AuraToolsExp.NativeReward.Tests` | 5 个文件，`Program.cs` 436 行 | 已按角色语义、内容规则、运行时验收和夹具拆分 |
| `Terrias-Dev.ElementalTests` | 162 行 | 保留为功能专项 |
| `Terrias-Dev.FamiliarTests` | 189 行 | 保留为功能专项 |
| `Terrias-Dev.RegistryTests` | 175 行 | 保留为 Spirit Registry 专项，由矩阵独立选择 |
| `Terrias-Dev.SpiritTests` | 103 行，20 条断言 | 新增 Spirit 概率、冷却、计划快照、效果、身份和生命周期行为专项 |
| `Terrias-Dev.ColumbinaTests` | 67 行，17 条断言 | 新增角色池、身份优先级、目标表现与 sender/status 所有权行为专项 |
| `Terrias-Dev.Tests` | `Program.cs` 1,832 行 | 正式承接原 Terrias C# here-string 行为 harness，当前 637 条断言，包含致命伤害后抽牌/发牌拒绝与原生队列清理 |

## 分阶段实施计划

### P0：修正调用图

状态：2026-08-07 已完成。

1. 共享消费者验证已删除重复的 Core 执行；该旧入口随后在 P1 退休。
2. `Test-TerriasArchitecture.ps1` 已移除 `Test-SpiritCapture.ps1` 调用。
3. `Test-TerriasCSharp.ps1` 已移除 Columbina、Elemental 的无条件调用。
4. shared release matrix 已支持 `core`、`domain`、`consumer`、`network`、
   `packaging`、`full-release` 以及更细的领域 profile 和 impact tag；无 selector
   时不再默认执行完整发布链。
5. Terrias 已建立可查询、可聚焦选择的矩阵；没有新增默认全套入口。

### P1：拆除源码快照单体

状态：2026-08-07 已完成。

1. `Test-TerriasArchitecture.ps1` 已只保留命名空间、依赖方向、Hook 隔离、
   CSV 入口和共享边界。
2. `Test-TerriasCSharp.ps1` 的行为 harness 已迁入正式 `Terrias-Dev.Tests`；
   PowerShell 实现快照已删除。
3. `Test-MainSharedFramework.ps1` 已删除；矩阵直接调用
   `Build-MainSharedConsumers.ps1`。
4. `Test-SharedArchitectureGuidelines.ps1` 已改为规则驱动的依赖扫描，不再
   描述私有类名、方法名和文件布局。
5. compatibility baseline 中 7 组源码 snippet 已全部删除。
6. Spirit、Columbina、Elemental 的私有实现锚点已分别迁到 C# 行为测试或
   结构化内容验证；专项 PowerShell 不再读取功能 C# 源码。

### P2：拆分 AuraToolsExp 与 AI 验证

状态：2026-08-07 已完成。

1. `Test-AuraToolsExp.ps1` 现只编排 Tool 行为和 Tool 自有内容；训练工件由
   `Test-AuraCombatTrainingArtifacts.ps1` 独立验证。
2. Combat AI 行为、知识、训练工件、仿真验收、worker 和发布档案已成为六个
   显式矩阵步骤，不再互相子调用。
3. Combat AI、Native Reward、Shared Core、AuraToolsExp 与 Audio Arbiter 的
   超大 `Program.cs` 已按领域拆文件，原命令入口保持不变；退休的仅是不能
   对应当前契约的 AuraToolsExp 私有源码快照断言。
4. Foundation worker 只属于 `foundation` 和 `full-release` profile；归档维护
   只属于 `full-release`。

### P3：迁移安全与网络源码扫描

状态：2026-08-07 已完成。

1. Core、CG、Audio 已覆盖 sender scope/authority、payload guard、重复抑制
   和生命周期清理；`network` profile 显式运行三组行为测试。
2. `Test-NetworkRpcAuthority.ps1` 已缩减为 payload 身份授权、裸 transport、
   server-bound marker 与统一 authority 注册扫描；每个 marker 必须在同一
   `Register(...)` 块同时出现类型 predicate 和 `BindServerSender`。
3. 三个架构入口统一使用 `architecture-boundary-rules.json`；写入口、共享依赖
   和内容/工具边界各自只有一个规则集。
4. 通用扫描发现并修复了 Solar Memory GameApi 裸 RPC 发送；AuraCg 私有 RPC
   hook 也已迁移到 `AuraRpcAuthorityRuntime`。

## 补充开发完成

状态：2026-08-07 已完成。

1. Spirit profile 已显式拆为内容、Registry、Runtime 三步，不存在隐藏子调用。
2. Columbina、Elemental、AuraToolsExp 的所属内容通过 CSV/JSON/schema 验证，
   运行时语义由正式 C# 行为项目承担。
3. Core、AuraToolsExp、Audio Arbiter 单体测试入口完成拆文件，测试命令保持稳定。
4. AuraSkin 安装器复用可测试的 manifest/source preflight policy；全局运行时复用
   可测试的协议范围兼容 policy，不再通过固定协议数字源码字符串验证。
5. Terrias 矩阵已删除 `source-contract` 标签，改为 `runtime-contract`；AuraSkin
   矩阵补充 `package-installation` 和 `protocol` impact tag。

## 删除或迁移判定

立即删除候选：完成迁移的旧名称、固定旧协议数字、已删除文件/API 的负向
扫描、具体私有方法顺序、与行为测试重复的源码 snippet。

先迁移后删除：RPC 权威、路径逃逸、Core 写入口、内容/工具所有权、重复抑制、
持久化恢复。这些仍是当前契约，只是需要从源码单词检查迁到行为或通用静态
规则。

继续保留：公共 API baseline、DLL 哈希、项目引用方向、结构化 JSON/CSV/schema
验证、所属 MOD 的当前内容身份，以及可重复执行的行为测试。

## 完成标准

- 已满足：产品和 shared 默认验证中不存在 `TestMods` 调用或构建。
- 已满足：两个矩阵中的每个正式入口都有 owner、category、cost、impact tag、
  profile 和唯一权威契约。
- 已满足：Full release 不通过隐藏子调用重复执行相同专项入口。
- 已满足：架构测试不描述具体功能算法或私有方法顺序。
- 已满足：正式产品范围内无退出条件的历史负向源码扫描为零；保留扫描只对应
  当前安全、资源解析、依赖或所有权边界。
- 已满足：修改单一共享域时，只运行该域行为、共享构建和真正受影响的消费者验证。

## 本轮验证记录

- 正式共享矩阵除 Foundation worker/归档维护外的 19 个步骤全部通过：Core
  1,246、CG 171、Skin 36、Audio 475、Combat AI 537、AuraToolsExp 1,340 条
  行为断言，以及知识、训练工件、仿真、架构、网络、Director、Native Reward、
  consumer 和 packaging 验证。
- 公共 API compatibility baseline 4,951 项通过；Terrias、SanGuoShaExp、
  AuraToolsExp 三个正式消费者构建通过，分发的 `Aura.Shared.dll` 哈希一致。
- Terrias `full-release` profile 13 个显式步骤全部通过：637 条主 C# 断言、
  Architecture、Content、Resources、Events、Spirit 三层、Columbina、Elemental、
  Familiar、Branding 和 shipped DLL 构建。
- 当前 `Managed/Witch.dll` 仍是已静态审查但未完成游戏烟雾验证的版本；Director
  gate 验证其保持 fail-closed 且不安装 Harmony prefix，不擅自扩充生产 allowlist。
- Foundation worker 和归档维护未运行，因为本轮没有 worker/归档改动，也不是
  正式发布候选；TestMods 按隔离约束未运行。另已验证 shared gate 对无 selector
  调用会直接拒绝，不再意外启动完整发布链。
