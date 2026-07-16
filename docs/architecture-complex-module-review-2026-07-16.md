# Aura/SunExp 复杂模块拆分评审

> 评审日期：2026-07-16  
> 评审状态：当前实现基线  
> 评审范围：`AuraSharedCore`、Aura 共享领域组件、`SunExp-Dev`、`AuraToolsExp-Dev`  
> 评审目的：判断复杂模块是否已经达到职责清晰、依赖单向、可独立测试和可安全演进的健康程度

## 1. 结论

当前架构已经形成健康的宏观依赖方向：Aura 核心层与共享领域层位于底部，SunExp 内容 MOD 与 AuraToolsExp 工具 MOD 是互不依赖的兄弟消费者。共享所有权、初始化隔离、资源注册和发布门禁也已经建立。

复杂模块内部的拆分尚未达到长期健康状态。共享 CG、共享音频、Solar Memory、AuraTools DamageMeter 和 AuraTools StarterDeck 仍存在职责聚合过多的协调器或运行时。它们可以继续开发，但修改成本、回归面和测试难度会随功能增长明显上升。

本次评审给出的总体判断是：

| 维度 | 结论 | 说明 |
| --- | --- | --- |
| 宏观分层 | 健康 | Core/Shared -> Content/Tool 依赖方向稳定 |
| 跨 MOD 所有权 | 健康 | SunExp 与 AuraToolsExp 无直接项目或源码依赖 |
| 初始化组织 | 健康 | Entry/RuntimeHooks 使用隔离步骤初始化 |
| Core 内部拆分 | 基本健康 | 存储、注册、缓存、Hook、调度和后台工作已有独立模块 |
| 共享领域运行时拆分 | 不足 | CG、Audio、BGM 等仍存在大型单文件运行时 |
| SunExp 复杂业务拆分 | 部分健康 | 已有多个服务和 Runtime，但 Solar Memory 主运行时仍聚合多条流程 |
| AuraToolsExp 功能拆分 | 部分健康 | DamageMeter 已有子域，但主 Runtime 仍承担捕获、生命周期和历史处理 |
| 测试对拆分的支持 | 不足 | 大量源码字符串断言保护结构，但缺少可支撑重构的纯行为测试 |

结论不是要求按行数机械拆文件，而是要求把不同变化原因、不同生命周期和不同依赖方向分离。

## 2. 评审判定标准

复杂模块达到健康程度，至少应满足以下条件：

1. 公共 Facade 或 Runtime 主要负责初始化、协调和委托，不直接实现资源解析、网络验证、Unity 表现和领域决策的全部细节。
2. 纯领域决策可以脱离 Unity、Witch、Mirror 和 Hook 上下文进行单元测试。
3. 网络协议、权限验证、重复抑制和本地表现之间有明确边界。
4. 资源注册、解析、预加载、缓存和播放生命周期可以分别定位和修改。
5. UI View、布局算法、Controller 和持久化配置不集中在同一个大型类型中。
6. 模块拆分后不需要通过全局静态状态或反射重新耦合。
7. 架构门禁检查稳定边界和依赖方向，不依赖某个实现必须永久存在于单一文件。

行数只作为定位信号。单一不变量驱动的 1000 行调度器可能仍然合理，而同时处理注册、网络、资源和 UI 的 1000 行 Runtime 通常已经需要拆分。

## 3. 量化盘点

以下统计只包含当前实现目录中的 C# 源文件，不包含 `obj` 等生成目录：

| 区域 | C# 文件数 | 超过 500 行 | 超过 800 行 | 超过 1200 行 |
| --- | ---: | ---: | ---: | ---: |
| SunExp-Dev | 399 | 35 | 17 | 4 |
| AuraToolsExp-Dev | 76 | 15 | 9 | 7 |
| Aura Core/Shared | 116 | 18 | 7 | 6 |

AuraToolsExp 的大型文件占比最高；共享层的大型文件数量较少，但影响所有消费者，单个问题的发布半径更大。

主要热点如下：

| 模块 | 约行数 | 约类型数 | 评审等级 |
| --- | ---: | ---: | --- |
| `AuraCgShared/AuraCgRuntime.cs` | 4250 | 26 | 高风险，必须拆分 |
| `AudioArbiterShared/AudioArbiterRuntime.cs` | 3276 | 23 | 高风险，必须拆分 |
| `AuraToolsDamageMeterRuntime.cs` | 2454 | 10 | 高风险，主 Runtime 需要变薄 |
| `AuraToolsStarterDeckRuntime.cs` | 2279 | 13 | 高风险，运行逻辑与 UI 应分离 |
| `SolarMemoryModeRuntime.cs` | 1913 | 3 | 高风险，Hook 层承担过多业务流程 |
| `BattleBgmArbiterRuntime.cs` | 1742 | 12 | 中高风险，可复用 Audio 拆分模式 |
| `HeartChangeControlService.cs` | 1397 | 2 | 中风险，保持状态核心，提取网络与目标策略 |
| `AuraToolsConfigModels.cs` | 1254 | 约 29 | 中风险，应按配置子域拆文件 |
| `AuraSharedFrameScheduler.cs` | 1252 | 约 14 | 暂不按行数拆核心算法 |
| `ModeChoiceLayoutRuntime.cs` | 1208 | 6 | 中风险，可提取布局算法和拖拽组件 |

## 4. 已达到健康程度的部分

### 4.1 内容 MOD 与工具 MOD 的依赖方向

`SunExp-Dev/SunExp.Dll.csproj` 与 `AuraToolsExp-Dev/AuraToolsExp.Dll.csproj` 都引用 `AuraSharedRuntime-Dev/Aura.Shared.csproj`，两者没有互相引用。源码中也没有 AuraToolsExp 导入 SunExp 内部命名空间或 SunExp 导入 AuraToolsExp 内部命名空间的情况。

这证明当前 `共享层/核心层 -> 内容 MOD/工具 MOD` 的主架构方向是成立的。

### 4.2 初始化组合根

`SunExp-Dev/Entry.cs`、`SunExp-Dev/Hooks/RuntimeHooks.cs` 和 `AuraToolsExp-Dev/Entry.cs` 使用命名步骤隔离初始化失败。单个功能初始化失败不会直接中断后续无关模块。

这类组合根可以继续保持集中，因为它们只声明启动顺序和错误边界，不应承载具体业务实现。

### 4.3 AuraSharedCore

Core 已经把存储、注册表、资源包、Hook、帧调度、后台工作、资源缓存、生命周期路由和诊断拆成独立文件。当前主要问题不在 Core 的目录组织，而在部分共享领域组件把协议、运行时和表现重新聚合到一个类型中。

### 4.4 AuraSkinShared

AuraSkinShared 已形成 `Models`、`GameApi`、`Hooks`、`Mechanics`、`Services` 等子层，可以作为其他共享领域组件拆分时的工程组织参考。

## 5. 高风险模块评审

### 5.1 AuraCgShared

`AuraCgShared/AuraCgRuntime.cs` 当前同时包含：

- `SkillCgArbiterRuntime` 公共入口和全局组件兼容检查；
- 注册表查询、卡包/角色/卡牌匹配和请求构造；
- 本地请求校验、联机身份校验、事件去重和战斗 session；
- 图片、序列帧、AssetBundle 和材质预加载；
- 播放队列和 generation 控制；
- Overlay Canvas、Image、动画、闪屏和清理；
- RPC sender、请求、广播和战斗 session 协议；
- 大量协议 DTO 和字符串枚举。

建议目标结构：

| 目标模块 | 职责 |
| --- | --- |
| `SkillCgArbiterRuntime` | 保留公共 Facade、全局组件发现和委托 |
| `SkillCgRegistryQueryService` | 注册项过滤、匹配和请求构造 |
| `SkillCgPlaybackCoordinator` | 队列、generation、播放生命周期 |
| `SkillCgOverlayPresenter` | Canvas、Image、动画和视觉清理 |
| `SkillCgPreloadService` | 图片、序列、Bundle 和材质预加载 |
| `AuraCgPlaybackPolicy` | 本地/远端请求验证、预算和去重 |
| `AuraCgNetworkRuntime` | RPC relay、sender authority、fight session |
| `AuraCgContracts` | 请求、快照、报告和协议常量 |

兼容性约束：当前全局组件依赖嵌套类型全名 `AuraCg.Shared.SkillCgArbiterRuntime+SkillCgArbiterComponent`。第一阶段应使用 `partial` 拆文件或保持嵌套类型身份，不应直接改为新的顶级组件类型。

### 5.2 AudioArbiterShared

`AudioArbiterShared/AudioArbiterRuntime.cs` 当前同时包含：

- 公共 Facade、全局组件兼容检查和 owner 注册；
- manifest 加载、迁移、条件构造和 provider 索引；
- provider resolution、冷却、替换和原音抑制；
- Unity AudioManager、AudioSource 和远端 fallback 播放；
- Career、Card、Buff、Vocal、LowHealth、BattleResult 等宿主 Hook；
- 本地/远端 presentation 去重和 sender authority；
- FileSoundProvider、ProviderRunner 和 RPC 类型。

建议目标结构：

| 目标模块 | 职责 |
| --- | --- |
| `AudioArbiterRuntime` | 公共入口、组件兼容和委托 |
| `AudioManifestLoader` | manifest 读取、schema 迁移和 provider 构造 |
| `AudioProviderResolver` | provider 匹配、优先级、冷却和结果描述 |
| `AudioPlaybackService` | AudioManager/AudioSource 播放和替换策略 |
| `AudioHookAdapter` | 宿主 Hook 解析并创建标准请求 |
| `AudioPresentationPolicy` | sender、时效、去重和所有权验证 |
| `AudioNetworkRuntime` | RPC 请求、广播和 fight session |
| `FileSoundProvider` | 文件加载、缓存和释放生命周期 |
| `AudioContracts` | manifest、请求、协议常量和 RPC DTO |

当前 `Aura.Shared.csproj` 只显式包含 `AudioArbiterRuntime.cs`。拆分前需要改为包含 `AudioArbiterShared/*.cs`，并继续通过共享 DLL 打包一致性门禁。

### 5.3 SolarMemoryModeRuntime

`SunExp-Dev/Hooks/SolarMemoryModeRuntime.cs` 当前承担五类职责：

1. 注册宿主 Hook 和模式入口。
2. 重写地图、生成固定节点和修改地图 UI。
3. 修复客户端 current node、同步数组和 NodeDice。
4. 处理战斗中止、战败、Boss 对话、最终结算和终局节点。
5. 过滤卡包、事件卡、角色牌组和 reserve pool。

建议将 `SolarMemoryModeRuntime` 收缩为 Hook 注册与协调器，并形成：

| 目标模块 | 职责 |
| --- | --- |
| `SolarMemoryMapProjectionRuntime` | 固定节点、地图槽和地图视觉 |
| `SolarMemoryMapSyncRepairService` | current node、同步数组和 NodeDice 修复 |
| `SolarMemoryBattleExitCoordinator` | 中止、战败和回图处理 |
| `SolarMemoryBossTransitionCoordinator` | Boss 前后对话和终局节点 |
| `SolarMemorySettlementCoordinator` | 最终结算和完成状态 |
| `SolarMemoryContentIsolationService` | 卡包、事件卡和角色牌组过滤 |

纯数组修复、节点选择和过滤规则应进入 Mechanics，以便脱离 Unity Hook 进行单元测试。地图实例和 UI 变更继续留在 Hooks。

### 5.4 AuraTools DamageMeter

DamageMeter 已有 `Capture`、`Model`、`Network`、`Resolution`、`SettlementCg` 等子目录，说明拆分方向正确。但 `AuraToolsDamageMeterRuntime` 仍同时负责：

- Hook 注册、开关和生命周期；
- UI 可见性、刷新和异常退避；
- 战斗/冒险/结算状态转换；
- 队伍快照、玩家名称和头像 PNG 编码；
- Hit/PureHp/HpSetter/Buff frame 捕获状态机；
- 捕获对象池和反射 accessor；
- 向 Resolution/Network 提交标准事件。

建议目标结构：

| 目标模块 | 职责 |
| --- | --- |
| `DamageMeterLifecycleRuntime` | 启停、战斗/冒险生命周期和 UI 可见性 |
| `DamageCapturePipeline` | Hook frame、配对、剪枝和标准事件生成 |
| `DamageCaptureHookAdapter` | ModHookContext 参数解析 |
| `AdventureDamageHistoryCoordinator` | 队伍快照、结算归档和历史恢复 |
| `DamageAvatarCache` | Sprite 读取、Texture 复制、PNG 编码和容量限制 |
| 现有 Resolution/Network | 继续负责归因和联机账本 |

主 Runtime 最终只应连接这些模块，不再持有所有 frame 集合、头像缓存和历史状态。

### 5.5 AuraTools StarterDeck

`AuraToolsStarterDeckRuntime.cs` 同时包含运行时牌组应用、Profile 解析、卡牌目录索引、图标缓存、编辑器窗口和角色 Profile 管理。

建议拆为：

| 目标模块 | 职责 |
| --- | --- |
| `StarterDeckApplicationRuntime` | 宿主 Hook 和 RoleTable 应用 |
| `StarterDeckEffectiveProfileResolver` | 本地覆盖与共享 Profile 解析 |
| `StarterDeckCardCatalog` | 卡牌、卡包、显示名、稀有度和费用索引 |
| `StarterDeckIconCache` | 图标加载和生命周期 |
| `AuraToolsStarterDeckEditor` | 牌组编辑 UI |
| `AuraToolsStarterDeckRoleManager` | 角色/Profile 管理 UI |

UI 类型至少应移入独立文件；Profile resolution 和 card catalog 应具有纯行为测试。

## 6. 中风险模块

### 6.1 BattleBgmArbiter

该模块规模已经超过单一运行时的舒适范围，但职责比 AudioArbiter 更集中。建议在 Audio 拆分模式稳定后复用相同边界，分离 provider resolution、播放状态、Hook adapter 和快照恢复。

### 6.2 HeartChangeControlService

心变控制涉及 Active 状态、友方槽位、原生行动队列抑制和恢复，这些不变量适合保留在同一协调器中。暂不建议按方法数量机械拆散。

可优先提取：

- `HeartChangeNetworkPolicy`：sender、token 和状态所有权验证；
- `HeartChangeTargetPolicy`：目标过滤解析和目标集合重写；
- `HeartChangeQueueAdapter`：原生行动队列快照、抑制和恢复。

Active/ReservedSlots 和状态结束事务仍由一个核心协调器维护。

### 6.3 ModeChoiceLayoutRuntime

该模块围绕一个 UI 场景，整体内聚度尚可。可把纯位置计算、边界相交和间距算法提取为 `ModeChoiceLayoutEngine`，把拖拽 MonoBehaviour 移到独立文件。Hook 注册、Unity Transform 应用继续保留在 Runtime。

### 6.4 AuraToolsConfigModels

运行时配置已经按 JSON 文件分域，但所有 C# DTO 集中在一个文件。建议按 Audio、MatchExperience、SkillCg、Skin、Logging 拆文件，保留 `AuraToolsRootConfig` 和 `ModuleFileConfig` 作为根契约。

## 7. 暂不建议拆分的模块

### 7.1 AuraSharedFrameScheduler

帧调度器虽然超过 1200 行，但主要围绕队列、延迟堆、owner 公平、帧预算、统计和 runner 生命周期这一组共同不变量。

可以把请求/报告 DTO 移入 `AuraSharedFrameSchedulerContracts.cs`，但队列记账、晋升和公平调度算法应保留在同一个内部模块，避免拆分后产生跨服务计数不一致。

### 7.2 大型 UI View

Blessing Picker、Starter Deck Window、Dimension Shop Native Skin 等大型 UI 文件不能只因行数拆分。只有在能够形成稳定的 View、Controller、数据适配器或可复用组件边界时才应处理。

## 8. 测试与门禁问题

当前门禁的优点是能够快速阻止依赖倒置、直接资源加载和关键兼容路径丢失。主要不足是大量断言直接读取固定源文件并搜索实现字符串。

典型影响：

- 把 `RpcAudioPresentationRequest` 移出 `AudioArbiterRuntime.cs` 会触发测试失败，即使行为和公共 API 完全不变；
- 把 Solar Memory 方法移入 Mechanics 会要求同步修改大量 `Test-SunExpArchitecture.ps1` 变量和字符串断言；
- Unity 运行时的 resolver、去重、状态迁移和 fallback 仍缺少等价的行为测试。

拆分前应补充以下测试面：

1. CG registry 匹配、请求规范化、网络预算和重复抑制的纯测试。
2. Audio provider 匹配、优先级、冷却、替换策略和 presentation 验证的纯测试。
3. Solar Memory 同步数组修复、固定节点选择和内容过滤的纯测试。
4. Damage capture frame 配对、归因输入和历史状态迁移测试。
5. StarterDeck Profile resolution、card catalog 和本地覆盖优先级测试。

架构门禁应逐步从“类型必须位于某文件”调整为“依赖方向、公共契约、禁止调用和行为结果必须成立”。

## 9. 分阶段实施路线

### 阶段 0：重构护栏

1. 为 CG、Audio、Solar Memory、DamageMeter、StarterDeck 补充纯行为测试。
2. 将 Audio/BGM/StarterDeck 的共享项目编译包含方式从单文件改为目录源文件集合。
3. 记录需要保持的公共类型、嵌套类型全名、BuildId、ProtocolVersion 和序列化字段。
4. 增加每个目标模块的行为基线和关键诊断日志基线。

### 阶段 1：无行为变化的源码组织

1. 使用 `partial` 或同命名空间独立类型拆分 Contracts、Network、Playback、Preload、HookAdapter。
2. 保持公共 Facade、方法签名、嵌套组件全名和初始化顺序不变。
3. 不同时修改协议、表现效果和缓存策略。
4. 更新源码结构门禁，使其允许合理文件拆分。

### 阶段 2：共享领域服务提取

1. 提取 CG registry query、playback policy 和 network policy。
2. 提取 Audio manifest、resolver、playback 和 presentation policy。
3. 使用同一模式处理 BattleBgm。
4. 对所有主消费者执行构建、协议和 DLL 打包一致性验证。

### 阶段 3：内容与工具复杂模块

1. 收缩 `SolarMemoryModeRuntime`。
2. 分离 DamageMeter capture、history、avatar 和 lifecycle。
3. 分离 StarterDeck application、resolver、catalog 和 UI。
4. 按功能域拆分 AuraTools 配置 DTO。

### 阶段 4：目录与文档整理

当类型职责稳定后，再把 Solar Memory、Endless、Companion、Projection 等已形成稳定子域的文件移动到子目录。目录调整不应与行为重构放在同一批次。

## 10. 每批验收条件

每个拆分批次必须满足：

1. SunExp 与 AuraToolsExp 仍无直接项目或源码依赖。
2. Shared/Core 不出现 SunExp 或 AuraToolsExp 业务 ID 和策略。
3. 公共 API、序列化字段、RPC command 类型和嵌套全局组件身份保持兼容；语义确实变化时才升级 BuildId/ProtocolVersion。
4. Facade/Runtime 只负责初始化、协调和委托，不重新复制提取后的实现。
5. 纯 resolver/policy/service 不依赖 Unity、Witch UI 或 ModHookContext。
6. 所有队列、缓存、provider、coroutine 和网络去重状态都有明确 owner 与清理生命周期。
7. `Aura.Shared.dll` 在所有打包消费者中保持哈希一致。
8. SunExp 架构测试、SunExp C# 测试、AuraToolsExp 测试、AuraSharedCore 测试和共享发布门禁全部通过。
9. 不以新增全局静态状态或反射调用作为模块拆分手段。
10. 性能计数、backlog、缓存估算和关键错误日志不因重构丢失。

建议把以下规模信号作为评审触发器，而不是硬编码门禁：

- 超过 800 行：检查是否包含多个变化原因；
- 超过 1200 行：必须记录保持单体的理由或拆分计划；
- 同一类型同时依赖 Unity UI、网络 RPC、文件系统和注册表：优先进行职责评审；
- 同一协调器持有三个以上独立生命周期缓存：优先提取所有权明确的子模块。

## 11. 风险控制

### 11.1 全局组件兼容

Aura CG、Audio、BGM 和 UI Guard 使用持久全局 GameObject 与反射兼容检查。拆分源码时不得无意改变组件的完整类型名。若必须改变，则需要同步升级协议并验证多 MOD 加载顺序。

### 11.2 静态状态迁移

大型 Runtime 中存在队列、去重集合、provider、fight token、generation 和缓存。迁移到子服务时要先明确唯一 owner，避免旧字段和新服务同时保留两份状态。

### 11.3 网络行为

RPC DTO、sender authority、事件时效、序列和 duplicate claim 必须作为同一协议批次验证。源码拆分阶段不应顺便改变网络语义。

### 11.4 Unity 生命周期

Coroutine runner、Overlay、AudioSource、AssetBundle 和运行时 Sprite 的创建与销毁必须保持原顺序。纯服务提取不能把 Unity 对象访问移动到后台线程。

### 11.5 测试迁移

先增加行为测试，再放宽固定文件字符串断言。不能先删除门禁，等重构完成后再补测试。

## 12. 最终评审决定

本次评审对当前架构作出以下决定：

- 接受当前宏观分层和共享所有权模型；
- 接受 AuraSharedCore 当前主要模块边界；
- 有条件接受复杂领域运行时继续维护，但不建议继续向 CG、Audio、Solar Memory、DamageMeter 和 StarterDeck 的主类直接增加新职责；
- 下一轮架构开发应从测试护栏和共享运行时的兼容拆文件开始；
- 文件拆分只是第一步，最终目标是形成可独立测试、依赖明确且拥有清理生命周期的服务边界。

当前复杂模块拆分完成度可视为约六成：结构方向正确，外围模块已经出现，但核心协调器仍需继续变薄。
