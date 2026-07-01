# SunExp 与 AuraToolsExp 架构与设计

最后更新：2026-07-01

本文解析当前仓库中 `SunExp`、`AuraToolsExp` 以及它们共用的
`Aura.Shared.dll` 运行时架构。范围覆盖已发布 Mod 目录、C# 开发目录、
共享运行时、注册表、资源包、网络权威与主要验证脚本。

## 1. 总览

SunExp 和 AuraToolsExp 不是两个彼此独立的孤岛。它们共享一套
`Aura.Shared.dll`，但各自保留清晰的所有权：

- `SunExp` 是内容型 Mod，负责卡牌、角色、事件、Solar Memory 玩法、
  视觉资源、音频、CG、初始牌组等实际内容。
- `AuraToolsExp` 是工具型 Mod，负责配置、查看、选择、复制、同步和运行
  工具功能，例如音频、皮肤、技能 CG、宴会 CG、初始牌组、保险箱、
  DPS 统计、Mod 同步等。
- `Aura.Shared.dll` 是二者共同使用的共享运行时，负责跨 Mod 的注册、
  存储、包安装、协议兼容、仲裁器、共享 UI 防护与若干领域组件。

构建和发布关系如下：

```text
SunExp-Dev/SunExp.Dll.csproj
  -> SunExp/Scripts/Entry.dll
  -> SunExp/Scripts/Aura.Shared.dll

AuraToolsExp-Dev/AuraToolsExp.Dll.csproj
  -> AuraToolsExp/Scripts/Entry.dll
  -> AuraToolsExp/Scripts/Aura.Shared.dll

AuraSharedRuntime-Dev/Aura.Shared.csproj
  -> 引入 AuraSharedCore、AuraAudioShared、AuraCgShared、AuraJourneyShared、
     AuraSkinShared、AuraOnlineShared、AuraLogShared 以及各类 Arbiter 组件
```

发布时，两个 Mod 中打包的 `Aura.Shared.dll` 必须来自同一个共享运行时构建，
并保持哈希一致。产品 Mod 通过项目引用使用 `AuraSharedRuntime-Dev`，不能把
共享源码私有编译进自己的 DLL。

## 2. 目录与制品边界

### 2.1 已发布 Mod 目录

`SunExp/` 是 SunExp 的发布目录，包含：

- `ModConfig.json`：Mod 元数据，当前为 `SunExp`，版本 `0.4.1`。
- `Data/`：卡牌、遗物、Buff、事件、敌人、职业等 CSV 数据。
- `Text/`：本地化文本。
- `Scripts/`：运行时 DLL，包含 `Entry.dll` 与 `Aura.Shared.dll`。
- `SharedResources/`：共享资源包、CG 注册表、音频资源等。
- `ModResource/`：视觉资源、贴图、Shader、VisualBundle 等。
- `starterdeck.registry.json`、`audio.registry.json`、`visual.registry.json`：
  领域注册表。

`AuraToolsExp/` 是 AuraToolsExp 的发布目录，包含：

- `ModConfig.json`：Mod 元数据，当前为 `AuraToolsExp`，版本 `0.3.1`。
- `Config/`：工具默认配置。
- `Scripts/`：运行时 DLL，包含 `Entry.dll` 与 `Aura.Shared.dll`。
- `SharedResources/`：工具自身的共享资源包与 CG 注册表。

### 2.2 C# 开发目录

`SunExp-Dev/` 是 SunExp 的 C# 实现，核心分层为：

- `Scripting`：CSV 脚本入口，提供给 XLua 调用。
- `GameApi`：面向游戏对象的安全封装与兼容门面。
- `Mechanics`：卡牌、Buff、遗物、Solar Memory 等玩法逻辑。
- `Features`：较完整的产品功能模块。
- `Hooks`：对游戏原生方法的 Hook 和运行时接线。
- `Infrastructure`：日志、资源缓存、调度器、配置索引、UI 工具等基础设施。
- `Network`：SunExp 的 RPC 权威与网络命令。

`AuraToolsExp-Dev/` 是 AuraToolsExp 的 C# 实现，核心分层为：

- 工具功能运行时：音频、皮肤、技能 CG、宴会 CG、保险箱、DPS、Mod 同步等。
- 配置与路径服务：集中管理工具配置、共享目录和本地资源。
- RPC 传输与权威：统一发送、分片、大小保护、服务端身份绑定。
- UI 与设置入口：把配置项和工具功能暴露给玩家。

`AuraSharedRuntime-Dev/` 构建 `Aura.Shared.dll`。它把多个共享源码根目录链接进
同一个程序集，是 SunExp 和 AuraToolsExp 的共同运行时基座。

## 3. 初始化流程

两个 Mod 都通过 `[ModInitialize]` 标记的 `Entry` 类进入运行时，并用
`AuraSharedHooks.RunStep` 包裹初始化步骤。这样可以把每个子系统的失败隔离在
有名称的步骤中，便于日志定位，也避免一个可选系统失败后直接拖垮全部初始化。

### 3.1 SunExp 初始化

SunExp 初始化顺序的主线是：

1. 注册 XLua 可见程序集，把 `SunExp.Dll.Scripting.*` 暴露给 CSV。
2. 初始化 `AuraSharedRuntime`，注册共享资源包和共享资源索引。
3. 初始化 SunExp 网络权威。
4. 加载 `visual.registry.json`，注册卡牌视觉皮肤和卡面特效。
5. 注册 CG、技能 CG、皮肤、音频、StarterDeck 等共享领域能力。
6. 初始化 Solar Memory 旅程定义。
7. 初始化帧调度器、运行时 Hook、特殊标签和各类视觉/玩法运行时。

SunExp 的初始化以内容系统为中心：先让共享运行时可用，再把 SunExp 拥有的
资源、注册表和玩法运行时挂入游戏。

### 3.2 AuraToolsExp 初始化

AuraToolsExp 初始化顺序的主线是：

1. 初始化共享核心与共享注册表。
2. 初始化 Journey、RPC 权威、工具配置、日志与资源引导。
3. 初始化 CG、UI 过渡保护、皮肤、音频、StarterDeck 等共享工具能力。
4. 初始化宴会 CG、保险箱、Mod 同步、DPS、技能 CG、设置 UI 等工具模块。

AuraToolsExp 的初始化以工具消费为中心：先建立配置和共享资源能力，再根据用户
配置启动不同工具模块。

## 4. Aura Shared 共享运行时

### 4.1 Core 层

`AuraSharedCore` 是共享运行时底座，职责是通用而非业务化的：

- 发现和复用全局共享组件。
- 检查协议版本、最低兼容版本、构建 ID 和公开方法形状。
- 提供 Owner、Shared、Runtime 三类存储。
- 提供带修订号的原子写入、备份、回滚和跨进程锁。
- 安装资源包，维护资源注册表。
- 提供变更流、诊断信息和操作日志。

Core 层不判断哪个 BGM、CG、皮肤、旅程或初始牌组应该获胜。它只提供稳定的
存储、注册、锁和协议能力。领域优先级、回退策略和业务校验属于领域共享组件。

### 4.2 领域共享组件

领域共享组件建立在 Core 之上，为某一类游戏能力提供类型化模型、校验、聚合和
仲裁。当前主要包括：

- `AuraAudioShared`、`AudioArbiterShared`、`BattleBgmArbiterShared`：
  管理音频资源、角色语音、卡牌音效、战斗 BGM 选择等。
- `AuraCgShared`：管理 CG 注册、激活、技能 CG、卡牌使用序列 CG、宴会 CG。
- `AuraJourneyShared`：管理跨 Mod 旅程定义，SunExp 的 Solar Memory 使用该层。
- `AuraSkinShared`：管理皮肤包、选择状态和远程同步。
- `StarterDeckArbiterShared`：管理初始牌组注册、候选聚合、优先级和应用。
- `AuraOnlineShared`：提供联机相关的共享模型和网络协作能力。
- `AuraLogShared`：提供日志目录和结构化记录。
- `UiRaycastSafetyShared`、`UiTransitionGuardShared`：提供 UI 防误触和过渡保护。

这些组件负责领域语义：什么是合法候选、谁拥有它、如何排序、能否被消费、何时
回退到默认项。

### 4.3 所有权与可变性

共享运行时把“谁拥有内容”和“谁使用内容”分开：

- 每个注册产物都有稳定的 `ownerModId`。
- 外部 Mod 注册的产物对工具只读，可以查看、选择、引用或复制。
- 工具不能直接修改其他 Mod 拥有的注册产物。
- 当只读注册产物被复制成本地配置后，新副本变成复制者拥有的本地可编辑产物。

例如，SunExp 可以注册 Wuna/Loneer 的初始牌组配置。AuraToolsExp 可以在工具 UI
中读取、展示和应用这些配置，也可以复制一份成为 AuraToolsExp 本地配置，但不能
直接改写 SunExp 的原始注册表。

### 4.4 存储与资源安装

共享存储分为三类：

- `Owner`：由所有者写入的配置，例如 AuraToolsExp 自己的工具配置。
- `Shared`：跨 Mod 共享的文档，只有一个权威写入者。
- `Runtime`：可重建运行态数据，不作为用户配置源。

写入通过 `expectedRevision` 做并发保护，通过临时文件和原子替换落盘。资源包安装
通过 `AuraSharedPackageEngine` 完成，并遵守资源锁、注册表锁、写互斥锁的顺序。

## 5. SunExp 架构

### 5.1 数据驱动入口

SunExp 的游戏内容主要由 CSV 驱动。CSV 的脚本列只允许调用
`CS.SunExp.Dll.Scripting.*`，例如：

- `CardScripts.Init/Use`
- `BuffScripts.Apply/Clear`
- `RelicScripts`
- `EventScripts`
- `WunaScripts`、`LoneerScripts`
- `BossScripts`
- `DuskPartnerScripts`、`StarClayDollScripts`

`Scripting` 层是脚本调用边界，不能直接依赖 `Hooks`，也不能绕过统一事件 API
直接注册原生事件。具体卡牌、Buff 和遗物逻辑通过处理器注册表分发，避免在脚本
入口中堆积顶层 `switch(id)`。

### 5.2 GameApi 与玩法层

`GameApi` 是游戏原生对象和 SunExp 玩法逻辑之间的安全封装层。`ExecutorApi`
作为兼容门面，把历史入口继续保留给旧逻辑，同时把具体能力委托给更聚焦的 API：

- 脚本变量与战斗变量。
- 目标选择。
- Buff 增删与溢出处理。
- 伤害与治疗。
- 事件注册与一次性 Token。
- Solar Memory 战斗接口。
- 场地和玩家状态接口。

玩法层位于 `Mechanics` 和 `Features`。这里承载卡牌、遗物、Buff、角色机制、
Solar Memory、视觉状态与特殊标签等实际业务。

### 5.3 Hook 层

`RuntimeHooks` 负责把 SunExp 功能接入游戏原生生命周期。它集中安装状态 Buff、
对话流、角色运行时、Solar Memory、奖励调整、视觉包校验、资源预加载、地图节点
动画、卡面视觉、Wuna 动画、星分 HUD、Loneer 等运行时。

Hook 层只负责接线和调用，不应把 CSV 脚本入口、资源加载策略或共享存储策略散落到
各处。复杂逻辑应下沉到玩法层、共享领域组件或基础设施层。

### 5.4 Solar Memory

Solar Memory 是 SunExp 的核心模式。它由几个子系统协作：

- `SolarMemoryJourneyApi`：向 `AuraJourneyRuntime` 注册旅程定义，定义准备、
  路线和 Boss 阶段，以及固定节点、事件槽、Boss 槽和掷骰策略。
- `SolarMemoryModeRuntime`：处理模式状态、地图修复、路径重写、结算衔接与
  运行时协调。
- `SolarMemoryRunLauncher`：负责进入和启动模式运行。
- `SolarMemoryMapVisualRuntime`：负责地图表现。
- `SolarMemorySettlementPresenter`：负责结算呈现。
- `SolarMemoryStarterDeckRuntime`：通过 `StarterDeckArbiterRuntime` 应用
  Solar Memory 专属初始牌组，并声明 SunExp 对该模式牌组的所有权。
- `RpcSolarMemoryRoleCommit`：通过服务端绑定发送者校验玩家角色提交，避免
  客户端伪造身份或重复提交。

Solar Memory 通过共享 Journey 与 StarterDeck 能力获得跨 Mod 可发现性，但模式
规则、角色提交、地图语义和结算仍由 SunExp 拥有。

### 5.5 视觉与资源

SunExp 的视觉系统以 `visual.registry.json` 为入口，注册：

- 纹理路径。
- 帧动画。
- 地图节点美术。
- Shader。
- 卡面视觉特效。
- VisualBundle。

`VisualRegistry` 负责读取注册表、规范化规格并提供查询。卡面视觉由
`CardVisualSkinRuntime`、`CardVisualSkinApplier`、`CardVisualEffectApplier`
等组件协作。运行时会使用视觉签名、缓存和下一帧重刷来减少重复工作。

资源加载通过 `SunExpResourceCache` 统一进入。架构测试要求它是
`ResourceLoader.Load/LoadAll` 的唯一集中入口，从而把缓存、预加载、统计和清理
能力收束到一处。

### 5.6 性能边界

SunExp 对低帧率和重复 Hook 工作有专门的基础设施：

- `SunExpPerformanceSettings`：定义 High、Balanced、Low、UltraLow 等质量档位和
  各类预算。
- `SunExpPerformanceCounters`：记录关键路径计数。
- `SunExpFrameScheduler`：按帧预算执行下一帧任务，支持 keyed work 去重。
- `SunExpActionEventRouter`：集中路由 Action/ActionAfter 事件，减少重复监听。
- `SunExpCardRefreshQueue`：把卡牌刷新和 `DataUpdate` 合并到下一帧。
- `SunExpResourcePreloader`：在预算允许时预热核心视觉资源。
- `SunExpConfigIndex`：替代热路径中的重复表扫描。

这些组件的共同设计目标是：不改变玩法语义，只减少重复事件、重复加载、重复刷新和
每帧峰值工作。

### 5.7 SunExp 网络权威

SunExp 的联机敏感命令通过 `SunExpRpcAuthorityRuntime` 和服务端绑定发送者处理。
Solar Memory 的角色提交不信任客户端传入的身份字段，而是在服务端接收路径绑定
真实发送者，再检查房间成员、角色归属、模式状态、提交 Token 和重复提交。

## 6. AuraToolsExp 架构

### 6.1 工具定位

AuraToolsExp 是共享内容的工具消费者和本地配置管理者。它不拥有 SunExp 的内容，
但可以读取 SunExp 注册到共享运行时的资源，并把用户选择保存为 AuraToolsExp 自己
的配置。

工具配置由 `AuraToolsConfigService` 管理。路径由 `AuraToolsPaths` 统一解析，
用户配置通过 `AuraSharedConfigStore` 写入共享 Owner 配置目录，并使用修订号保护
并发写入。

### 6.2 音频工具

`AuraToolsAudioRuntime` 初始化音频共享运行时和 BGM 仲裁器，然后根据配置注册工具
侧的音频提供者。它既可以使用 AuraToolsExp 的默认资源，也可以把本地配置中的角色
语音、卡牌音效和 BGM 候选交给共享仲裁器。

SunExp 自己也注册 `audio.registry.json`。两者的边界是：SunExp 提供内容型音频
资产，AuraToolsExp 提供工具侧选择、覆盖和播放策略。

### 6.3 皮肤工具

`AuraToolsSkinRuntime` 初始化皮肤共享运行时，注册 AuraToolsExp 自己的皮肤包，
监听本地选择变化，并在启用同步时通过 AuraToolsExp 的 RPC 传输广播远程选择。
皮肤所有权和选择状态由共享皮肤组件管理，工具运行时负责 UI、配置和远程传播。

### 6.4 初始牌组工具

`AuraToolsStarterDeckRuntime` 使用 `StarterDeckArbiterRuntime` 读取注册配置和本地
配置，解决候选优先级并应用牌组。它支持：

- 读取其他 Mod 注册的只读初始牌组。
- 使用 AuraToolsExp 的全局或角色本地配置。
- 把只读注册配置复制为本地可编辑配置。
- 在 SunExp Solar Memory 或外部所有者已声明的场景中跳过工具覆盖。

这保证了工具可以增强普通世界模拟体验，但不会抢占 SunExp 对 Solar Memory 专属
初始牌组的控制权。

### 6.5 技能 CG 与宴会 CG

`AuraToolsSkillCgRuntime` 通过 `SkillCgArbiterRuntime` 接入技能 CG 播放。它可以
消费共享 CG 注册表中的 `cardUse` 候选，也可以使用 AuraToolsExp 本地规则。播放前
会检查消费者是否有权限播放对应来源的 CG。

`AuraToolsFeastRuntime` 读取共享 CG 注册表中 `kind == "feast"` 的候选，按角色和
激活状态筛选，选择显式配置或最高优先级候选，再通过技能 CG 仲裁器播放。SunExp
在自己的 CG 注册表中提供 Wuna/Loneer 宴会 CG，并标记为可由 AuraToolsExp 管理。

### 6.6 保险箱、DPS 与 Mod 同步

`AuraToolsSafeBoxRuntime` 扩展保险箱使用体验，并保持存档兼容。

`AuraToolsDamageMeterRuntime` 负责战斗伤害统计、状态记录、UI 展示和联机快照。
网络传播使用压缩快照、最小快照或状态快照，避免把完整历史作为网络负载发送。出
战外历史通过共享配置存储持久化。

`AuraToolsModSyncRuntime` 负责房主向成员同步 Mod 清单。大负载通过分片传输，带有
大小预算、活动缓冲上限、TTL 和校验和。

### 6.7 AuraToolsExp 网络权威

AuraToolsExp 的 RPC 发送统一通过 `AuraToolsRpcTransport`。该层负责：

- UTF-8 字节长度测量。
- Mirror 字符串硬限制保护。
- 软限制告警。
- 大负载分片。
- 主线程派发。

服务端命令实现 `IAuraToolsServerBoundRpcCommand`，由
`AuraToolsRpcAuthorityRuntime` 在服务端接收路径绑定真实发送者。工具命令不能信任
payload 中自报的身份。

## 7. SunExp 与 AuraToolsExp 的协作模式

### 7.1 内容注册，工具消费

SunExp 把内容注册到共享运行时，例如：

- `SharedResources/package.json` 注册资源包和能力。
- `SharedResources/cg.registry.json` 注册技能 CG、卡牌使用序列 CG 和宴会 CG。
- `audio.registry.json` 注册音频提供者。
- `starterdeck.registry.json` 注册初始牌组。
- `visual.registry.json` 注册视觉资产。

AuraToolsExp 读取这些注册产物，用工具配置决定是否启用、如何展示、是否复制为本地
配置以及如何播放或应用。工具消费不会改变 SunExp 的源注册表。

### 7.2 典型链路

技能 CG 链路：

1. SunExp 在 CG 注册表中声明内容。
2. 共享 CG 运行时聚合候选。
3. AuraToolsExp 的技能 CG 工具根据配置和激活状态发起播放请求。
4. `SkillCgArbiterRuntime` 统一仲裁播放。

宴会 CG 链路：

1. SunExp 声明 Wuna/Loneer 的宴会 CG，并允许 AuraToolsExp 管理。
2. AuraToolsExp 读取 `feast` 候选。
3. 工具按角色、激活状态、显式选择和优先级选择 CG。
4. 播放请求仍走共享 CG 仲裁器。

初始牌组链路：

1. SunExp 注册只读牌组配置。
2. AuraToolsExp 聚合注册牌组和本地牌组。
3. 玩家可选择、应用或复制为本地可编辑配置。
4. Solar Memory 等 SunExp 拥有的模式仍由 SunExp 的牌组运行时控制。

音频链路：

1. SunExp 注册内容型音频。
2. AuraToolsExp 注册工具型或用户配置型音频候选。
3. 共享音频和 BGM 仲裁器根据领域规则选择最终播放项。

## 8. 设计约束

当前架构依赖以下约束保持稳定：

- CSV 脚本列只调用 `CS.SunExp.Dll.Scripting.*`。
- `Scripting` 层不引用 `Hooks`，不直接接触帧调度器，不直接注册原生事件。
- 卡牌、Buff、遗物入口使用处理器注册表分发。
- 游戏对象操作通过 `GameApi` 或聚焦 API 包装。
- 资源加载集中到 `SunExpResourceCache`。
- 热路径配置读取使用 `SunExpConfigIndex` 等索引结构，避免重复表扫描。
- 共享写入通过 `AuraSharedConfigStore`、`AuraSharedRegistry` 或
  `AuraSharedPackageEngine`。
- 外部注册产物只读；复制后才成为本地可编辑产物。
- 联机敏感命令使用服务端绑定发送者，不信任客户端 payload 身份。
- 大 RPC payload 必须经过大小保护和必要分片。
- UI 叠层和过渡使用共享 UI 安全组件，避免误触底层游戏 UI。
- 初始化步骤使用 `RunStep` 隔离失败。

## 9. 验证与发布门禁

架构相关验证脚本集中在 `tools/`：

- `Test-SunExpArchitecture.ps1`：检查 SunExp 分层、脚本入口、资源加载、性能基础
  设施、Solar Memory 边界等。
- `Test-NetworkRpcAuthority.ps1`：检查 SunExp 和 AuraToolsExp 的 RPC 权威、发送
  路径、payload 大小保护和分片策略。
- `Test-SharedArchitectureGuidelines.ps1`：检查共享架构规则。
- `Test-SharedReleaseGate.ps1`：检查共享协议、核心契约和发布矩阵。
- `Test-SharedDllPackaging.ps1`：检查消费者项目引用共享运行时并打包同一份
  `Aura.Shared.dll`。
- `Test-AuraToolsExp.ps1`：检查 AuraToolsExp 主要工具行为。
- `Test-SunExpCSharp.ps1` 与 `Build-SunExpDll.ps1`：分别验证 SunExp C# 测试和
  DLL 构建。

文档变更通常不需要重新构建 DLL，但涉及架构解释的代码修改应至少运行对应架构门禁。

## 10. 扩展建议

新增 SunExp 内容时：

- 先在 CSV、文本和资源目录声明内容。
- CSV 脚本列只接入 `Scripting`。
- 玩法实现放入对应 Mechanic 或 Feature。
- 若资源需要跨 Mod 被发现，添加对应共享注册表或资源包条目。

新增共享能力时：

- Core 只放通用存储、注册、锁和协议能力。
- 领域语义放入独立共享组件。
- 产品 Mod 通过注册表或适配器接入，不私改 Core 业务逻辑。

新增 AuraToolsExp 工具时：

- 配置进入 `AuraToolsConfigService`。
- 路径通过 `AuraToolsPaths`。
- 共享内容按只读注册产物消费。
- 用户编辑内容写入 AuraToolsExp Owner 配置。
- 联机功能必须通过 AuraToolsExp RPC 传输和权威绑定。

新增网络命令时：

- 服务端命令实现对应 server-bound 接口。
- 发送统一通过 transport。
- payload 身份只作数据，不作权威来源。
- 大对象使用快照压缩、摘要或分片，不传完整运行历史。

