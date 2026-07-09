# SunExp 与 AuraToolsExp 术语表

最后更新：2026-07-01

本术语表用于配合 `sunexp-auratoolsexp-architecture-and-design.md` 阅读。

| 术语 | 范围 | 含义 |
| --- | --- | --- |
| SunExp | 产品 Mod | 内容型 Mod，提供 Wuna/Loneer、卡牌、遗物、Buff、Solar Memory、视觉、音频、CG 等内容。 |
| AuraToolsExp | 工具 Mod | 工具型 Mod，提供配置、音频、皮肤、技能 CG、宴会 CG、初始牌组、保险箱、DPS、Mod 同步等功能。 |
| Aura.Shared.dll | 共享运行时 | SunExp、AuraToolsExp 等消费者共同打包的共享程序集。发布时各消费者中的 DLL 应保持同源和哈希一致。 |
| AuraSharedRuntime-Dev | 构建目录 | 构建 `Aura.Shared.dll` 的项目目录，通过链接源码引入 Core 和多个领域共享组件。 |
| AuraSharedCore | 共享 Core | 提供全局组件发现、协议兼容、共享存储、资源安装、注册表、锁、变更流和诊断能力。 |
| 领域共享组件 | 共享层 | 针对音频、CG、皮肤、旅程、初始牌组等领域提供模型、校验、聚合和仲裁。 |
| Product Mod | 架构角色 | 生产具体内容或玩法的 Mod，例如 SunExp。 |
| Tool Mod | 架构角色 | 消费、配置、复制或管理共享内容的工具 Mod，例如 AuraToolsExp。 |
| ownerModId | 所有权 | 注册产物的所有者标识，决定谁可以编辑源数据。 |
| 注册产物 | 共享资源 | 通过注册表或资源包声明给共享运行时的内容，例如 CG、音频、皮肤、牌组。 |
| 本地可编辑产物 | 工具配置 | 工具从只读注册产物复制出的本地副本，所有权归复制者。 |
| ContentOwned | CG/资源语义 | 表示资源由内容 Mod 拥有，工具通常只能消费或选择。 |
| ToolManaged | CG/资源语义 | 表示资源可由工具 Mod 管理或作为工具候选使用。 |
| Owner 存储 | 共享存储 | 由所有者写入的配置范围，例如 AuraToolsExp 自己的工具设置。 |
| Shared 存储 | 共享存储 | 跨 Mod 共享的文档范围，通常只有一个权威写入者。 |
| Runtime 存储 | 共享存储 | 可重建的运行态数据范围，不作为用户配置源。 |
| expectedRevision | 共享存储 | 写入时的修订号校验，用于避免并发覆盖。 |
| Resource Package | 共享资源 | 资源包声明，描述某个 Mod 提供的共享资源目录、版本、能力和安装目标。 |
| Registry Manifest | 共享注册 | 领域注册表文件，例如 `cg.registry.json`、`audio.registry.json`、`starterdeck.registry.json`。 |
| logicalId | 共享资源 | 资源包或资源条目的逻辑标识，通常与 owner 一起形成稳定身份。 |
| Operation Log | 共享诊断 | 共享运行时写入的结构化操作记录，用于追踪安装、写入、冲突和恢复。 |
| RunStep | 初始化 | `AuraSharedHooks.RunStep` 包裹的初始化步骤，用名称隔离和记录子系统失败。 |
| ModInitialize | 游戏入口 | Mod DLL 的初始化标记，游戏加载时调用对应入口。 |
| Entry.dll | 发布制品 | 每个 Mod 自己的运行时 DLL，放在发布目录的 `Scripts/` 下。 |
| Managed | 依赖目录 | 仓库中的游戏和第三方程序集引用来源，供 C# 项目编译使用。 |
| CSV 脚本列 | SunExp 数据 | `SunExp/Data` 中调用 C# 脚本入口的列，只应调用 `CS.SunExp.Dll.Scripting.*`。 |
| Scripting | SunExp 层级 | 面向 XLua/CSV 的脚本入口层，负责参数解析和分发。 |
| GameApi | SunExp 层级 | 游戏对象和玩法逻辑之间的安全封装层。 |
| ExecutorApi | SunExp 兼容层 | 历史脚本兼容门面，把能力委托给更聚焦的 API。 |
| Mechanics | SunExp 层级 | 卡牌、Buff、遗物、角色与模式等核心玩法逻辑。 |
| Features | SunExp 层级 | 由多个玩法和运行时组合出的完整功能模块。 |
| Hooks | SunExp 层级 | 接入游戏原生生命周期的方法 Hook 和运行时接线层。 |
| Infrastructure | SunExp 层级 | 日志、调度、缓存、索引、UI 工具、性能统计等基础设施。 |
| Network | SunExp 层级 | SunExp 的 RPC 权威、发送者绑定和网络命令。 |
| 处理器注册表 | SunExp 分发 | Card、Buff、Relic 等入口使用的字典式分发结构，避免顶层 `switch(id)` 膨胀。 |
| ScriptEventApi | SunExp 事件 | 对脚本事件注册、临时事件、Token 化事件的统一封装。 |
| SunExpIds | SunExp 常量 | SunExp 中集中定义的卡牌、Buff、模式、标签等 ID。 |
| SunExpResourceCache | SunExp 资源 | 统一的资源加载入口和缓存层，是 `ResourceLoader.Load/LoadAll` 的集中使用点。 |
| SunExpConfigIndex | SunExp 性能 | 热路径配置索引，用于替代重复表扫描。 |
| SunExpPerformanceSettings | SunExp 性能 | 统一性能开关和固定预算配置，不再按视觉效果质量分档。 |
| SunExpFrameScheduler | SunExp 性能 | SunExp 的调度门面，把 keyed work 去重委托给 AuraSharedFrameScheduler。 |
| SunExpActionEventRouter | SunExp 性能 | 集中路由原生 Action/ActionAfter 事件，减少重复监听。 |
| SunExpCardRefreshQueue | SunExp 性能 | 合并卡牌刷新和 `DataUpdate`，降低同帧重复刷新。 |
| SunExpResourcePreloader | SunExp 性能 | 在预算允许时预热核心视觉资源。 |
| VisualRegistry | SunExp 视觉 | 读取并规范化 `visual.registry.json` 的视觉注册表。 |
| VisualBundle | SunExp 视觉 | 成组打包的视觉资源集合，例如卡面、贴图、Shader 或效果资源。 |
| CardVisualSkin | SunExp 视觉 | 卡牌皮肤运行时和应用器相关能力。 |
| CardVisualEffect | SunExp 视觉 | 卡面特效注册和应用能力，例如箔光、星尘等效果。 |
| WunaOrbitFire | SunExp 视觉 | Wuna 相关的环绕火焰表现运行时。 |
| StarScore | SunExp 视觉/玩法 | 星分相关运行时和 HUD 表现。 |
| Solar Memory | SunExp 模式 | SunExp 的核心模式，包含旅程、地图节点、初始牌组、角色提交和结算。 |
| Journey | 共享旅程 | 由 `AuraJourneyRuntime` 管理的跨 Mod 旅程定义。 |
| RouteGraph | Solar Memory | Solar Memory 地图路线和层级结构。 |
| MapNode | Solar Memory | 地图上的节点，可能代表事件、战斗、奖励或 Boss。 |
| FixedNode | Solar Memory | 固定出现或固定语义的路线节点。 |
| NodeDice | Solar Memory | 节点选择、事件或路线中的掷骰策略。 |
| StarterDeckArbiter | 共享牌组 | 初始牌组候选聚合、优先级选择和应用组件。 |
| StarterDeck Profile | 共享牌组 | 初始牌组配置项，可能由 SunExp 注册，也可能由 AuraToolsExp 本地创建。 |
| Claim | 共享牌组 | 对某个模式或上下文中的牌组控制权声明。 |
| SolarMemoryRoleCommit | SunExp 网络 | Solar Memory 角色提交 RPC 命令，通过服务端绑定发送者校验身份。 |
| Server-bound RPC | 网络权威 | 必须在服务端接收路径绑定真实发送者的 RPC 命令。 |
| Bound Sender | 网络权威 | 服务端根据接收上下文绑定的真实发送者身份，不来自客户端 payload。 |
| Payload Guard | AuraTools 网络 | 对 RPC payload 做 UTF-8 字节长度测量、硬限制和软限制保护。 |
| Chunked Transport | AuraTools 网络 | 大 payload 分片发送与主线程派发机制。 |
| AuraCgRegistry | 共享 CG | CG 注册表运行时，聚合技能 CG、卡牌使用 CG、宴会 CG 等。 |
| SkillCgArbiter | 共享 CG | 技能 CG 播放仲裁器，统一处理播放请求和候选选择。 |
| SkillCgRequest | 共享 CG | 一次 CG 播放请求，包含触发、来源、候选、同步等信息。 |
| Feast CG | 共享 CG | 宴会场景使用的 CG，SunExp 提供 Wuna/Loneer 候选，AuraToolsExp 可消费。 |
| CardUse CG | 共享 CG | 卡牌使用时触发的序列 CG。 |
| AuraAudioRuntime | 共享音频 | 音频共享运行时，聚合音频提供者和播放请求。 |
| AudioArbiter | 共享音频 | 音频候选仲裁组件。 |
| BattleBgmArbiter | 共享音频 | 战斗 BGM 候选仲裁组件。 |
| AuraSkinRuntime | 共享皮肤 | 皮肤包、选择状态和同步能力的共享运行时。 |
| UiTransitionGuard | 共享 UI | UI 过渡保护组件，避免过渡态误触和状态错乱。 |
| UiRaycastSafety | 共享 UI | UI 叠层射线防护组件，避免工具窗口误穿透到底层游戏 UI。 |
| AuraToolsConfigService | AuraTools 配置 | AuraToolsExp 的集中配置服务，负责加载、保存、修订号和变更通知。 |
| AuraToolsPaths | AuraTools 配置 | AuraToolsExp 的路径解析服务，统一处理共享目录和工具目录。 |
| AuraToolsAudioRuntime | AuraTools 功能 | AuraToolsExp 的音频工具运行时。 |
| AuraToolsSkinRuntime | AuraTools 功能 | AuraToolsExp 的皮肤工具运行时和远程选择同步。 |
| AuraToolsStarterDeckRuntime | AuraTools 功能 | AuraToolsExp 的初始牌组工具运行时。 |
| AuraToolsSkillCgRuntime | AuraTools 功能 | AuraToolsExp 的技能 CG 工具运行时。 |
| AuraToolsFeastRuntime | AuraTools 功能 | AuraToolsExp 的宴会 CG 工具运行时。 |
| AuraToolsSafeBoxRuntime | AuraTools 功能 | AuraToolsExp 的保险箱扩展运行时。 |
| AuraToolsDamageMeterRuntime | AuraTools 功能 | AuraToolsExp 的 DPS 统计运行时。 |
| AuraToolsModSyncRuntime | AuraTools 功能 | AuraToolsExp 的 Mod 清单同步运行时。 |
| AuraToolsRpcTransport | AuraTools 网络 | AuraToolsExp 的统一 RPC 发送、分片和主线程派发入口。 |
| AuraToolsRpcAuthorityRuntime | AuraTools 网络 | AuraToolsExp 的服务端 RPC 发送者绑定和权威校验入口。 |
| IAuraToolsServerBoundRpcCommand | AuraTools 网络 | 表示命令必须在服务端绑定发送者后执行的接口。 |
| MustSame | Mod 配置 | `ModConfig.json` 中要求联机双方 Mod 一致的配置项。 |
| Architecture Gate | 验证 | 架构门禁脚本，例如 `Test-SunExpArchitecture.ps1`。 |
| Shared Release Gate | 验证 | 共享运行时发布门禁，检查协议、规则、DLL 打包和主要消费者。 |
