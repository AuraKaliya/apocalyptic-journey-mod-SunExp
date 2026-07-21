# SunExp 模块覆盖矩阵

> 状态：持续维护的覆盖清单；批次 A、B 已完成  
> 目的：确认“写什么、证据在哪里、如何接入游戏主体”，不替代模块正文。

## 1. 当前规模基线

### 1.1 内容交付层

- 卡牌：56（其中 `Card/sunexp.csv` 50，乌娜 3，洛奈尔 1，深渊诅咒 2）。
- Buff：32。
- 遗物：13。
- 卡包：5。
- Data 类型还包括 Blessing、Career、Dialogue、EnchTag、Enemy、EnemyCard、EventList、Hard、Level、Map、Partner、PartnerCard、RoleData。
- 注册表/配置包括音频、视觉、起始卡组、使魔祝福、伙伴意图、无尽深渊配置与进化特征、百变角色裁切、共享资源和 CG 注册表。

数量来自当前仓库盘点脚本；正式“内容实体清单”将逐项记录完整 id，而不是只保存统计数。

### 1.2 SunExp C# 实现层

| 层 | 当前生产文件数 | 覆盖要求 |
| --- | ---: | --- |
| `Entry.cs` | 1 | 加载链与共享初始化章节 |
| `Scripting` | 13 | 所有 CSV 可调用入口映射 |
| `GameApi` | 48 | 宿主 API、反射兼容和流程 facade 映射 |
| `Mechanics` | 143 | 按业务模块归属，禁止形成“其他”大类 |
| `Features` | 1 | Skill CG 模块 |
| `Hooks`，含 UI/Visual | 128 | Hook 目标、生命周期、UI/视觉归属 |
| `Network` | 14 | RPC、发送者绑定、状态同步归属 |
| `Infrastructure` | 13 | id、日志、性能、调度和诊断章节 |

## 2. 结构层覆盖

| 结构模块 | 主要源码 | 接入方式 | 宿主/共享参考 | 目标文档 | 状态 |
| --- | --- | --- | --- | --- | --- |
| MOD 初始化 | `Entry.cs` | `[ModInitialize]`、XLua 程序集注册、隔离步骤 | `Witch.ModInitialize`、`ScriptExecutor.luaEnv`、AuraShared hooks/core | 加载链、整体架构 | 已盘点，待逐方法反编译核验 |
| CSV 脚本边界 | `Scripting/*Scripts.cs`、`SunExp/Data/*` | `CS.SunExp.Dll.Scripting.*` | `ScriptExecutor`、官方 CSV 脚本形态 | 加载链、基础内容模块 | 已盘点，待生成入口清单 |
| 游戏 API 适配 | `GameApi/*` | 直接 API、反射兼容、确定性回退 | `Witch`、`Witch.Core`、`Managed/` | C# 分层、游戏主体接入 | 已盘点，待签名矩阵 |
| 业务机制 | `Mechanics/*` | 被脚本、Hook 或 Feature 调用 | 通过 GameApi 间接接入 | 各功能模块 | 已按文件名初分域 |
| 功能运行时 | `Features/SkillCg` | Entry 初始化、共享 CG 请求 | `AuraCgShared` | 音频/BGM/皮肤/CG | 已盘点 |
| Hook 与生命周期 | `Hooks/*` | AuraShared before/after routed hooks、事件监听 | `Witch` UI/战斗/地图类型 | 游戏主体接入、各模块 | 已盘点，待完整目标清单 |
| UI | `Hooks/Ui/*` | 独立 Canvas、宿主 UI 挂载、池化与清理 | `UIManager`、`UIBase`、Unity UI | 视觉/UI | 已盘点 |
| 视觉 | `Hooks/Visual/*` | registry、AssetBundle、材质/Shader、表现附着 | 卡牌 UI、Unity 渲染对象 | 视觉/UI | 已盘点 |
| 网络 | `Network/*` | Mirror RPC、sender binding、快照/提交 | `Mirror`、游戏 Map/Role 网络流 | 网络与同步 | 已盘点，待逐 RPC 场景表 |
| 基础设施 | `Infrastructure/*` | id、日志、性能计数、帧与生命周期调度 | Aura 调度/日志基础 | 性能兼容诊断 | 已盘点 |

## 3. 功能模块覆盖

表中的“宿主锚点”首先来自当前 Hook 目标和 API 使用。正式正文仍需打开对应反编译方法体并确认上下游流程。

| 功能域 | 内容/配置入口 | SunExp 实现入口 | Aura 共享依赖 | 当前宿主锚点 | 计划文档 | 优先级 |
| --- | --- | --- | --- | --- | --- | ---: |
| 卡牌、Buff、遗物与卡包 | `Data/Text/Card`、`Buff`、`Relic`、`CardPack` | `CardScripts`、`BuffScripts`、`RelicScripts`、Card/Buff/Damage API、各 Handler Registry | shared hooks/log/scheduler | `ScriptExecutor`、`CommonCardItem`、`AttackCardItem`、`SkillItem`、`FightPlayer` | 模块 01 | P0 |
| 场地、Hard 标签与战斗路由 | `Buff`、`Hard`、`EnchTag` | `FieldRuntime`、`SunExpHardTagRuntime`、Action/Card/Status/Lifecycle Router | shared routed hooks、frame scheduler | `Fight_Start`、`FightPlayer.TurnInit`、`ScriptExecutor.SetStatus/RunScript` | 模块 02 | P0 |
| 乌娜与白曜体系 | `Career/RoleData/Card/Buff` 的 wuna 行 | `WunaScripts`、`WunaRoundRadianceState`、Solar 系服务、动作动画、轨道火 | audio、skin、UI safety | 战斗开始/回合/行动事件、CardItem 使用、角色动画对象 | 模块 03 | P1 |
| 洛奈尔、晨星与星谱 | loneer Career/RoleData/Card，晨星卡包 | `LoneerScripts`、`MorningStarCardScripts`、StarScore/Miracle/StarStone 服务和 HUD | shared hooks、UI safety、scheduler | `CommonCardItem.OnBeginDrag/OnEndDrag`、`AttackCardItem`、战斗边界 | 模块 04 | P1 |
| 日耀回忆模式入口与准备 | RoleData、EventList、Map、starter deck、Blessing | `SolarMemoryRunLauncher`、Setup/Preparation/StarterDeck/BlessingPicker、RoleCommit API | Journey、StarterDeck Arbiter、UI guard | `ModeChoiceUI`、`RoleTable.Init`、`MapManager.MapUIStart`、`MapSelectUI.Start` | 模块 05 | P0 |
| 日耀回忆地图与结局 | EventList、Map、Enemy、EnemyCard | `SolarMemoryModeRuntime`、MapNodePool、StoryGate、Finale、Reward/Settlement | Journey、shared hooks、online authority | `NormalMapManager.RandomGenerate/GeneratrMap/MapItemInit/ReadyToChangeMap`、`MapManager` RPC、`Fight_Win/Escape/Loss` | 模块 05 | P0 |
| 无尽之海地图与运行状态 | endless sea 配置、Map/Enemy/奖励数据 | `EndlessSea*Runtime`、FloorPlanner、MapBuilder、RunStateStore | shared hooks、UI guard | `ModeChoiceUI`、`NormalMapManager`、`MapSelectUI`、`MapManager` | 模块 06 | P0 |
| 无尽深渊机制 | abyss 配置、进化特征、Blessing、诅咒卡、敌人 | `EndlessAbyss*Service`、ledger、reward、shock/milestone UI | shared hooks、online/scheduler | 地图显示、战斗奖励、敌人意图、战斗结算 | 模块 07 | P0 |
| 伙伴与使魔 | Partner、PartnerCard、familiar registries | `DuskPartnerScripts`、Companion*、Familiar* 服务与运行时 | UI guard、shared hooks | `HouseManager`、`GameEntryUI.NormalGame`、`ScriptExecutor.SetStatus` | 后续模块，编号待定 | P1 |
| 百变 | polymorph 配置、卡牌/Buff | `Polymorph*Service/Runtime`、Role registry、UI、RPC visual state | shared hooks、UI safety | `SkillItem.TrueUse`、战斗结束边界、角色/UI 对象 | 模块 09 | P1 |
| 投影 | 投影卡牌、EnemyCard、伙伴意图 | `ProjectionScripts`、Projection* services/runtime、RPC companion | shared hooks、online patterns | `CommonCardItem.OnBeginDrag/UseCardDirectly`、`FightPlayer.TurnInit`、战斗边界 | 模块 09 | P1 |
| 精灵球与精灵 | 精灵球、动态精灵卡、精灵意图/捕获注册表 | `SpiritCapture*`、`SpiritCardFactory`、`SpiritSummon*`、Spirit runtime/RPC | shared hooks、online authority、payload budget | `AttackCardItem.TrueUse`、`StatusManager.CheckDead/EnemyDead`、`EnemyManager.AddEnemy`、`DictionaryUI`、战斗边界 | 模块 08 | P0 |
| 心变 | 心变卡牌、Buff、EnemyCard | `HeartChangeScripts`、Control/Intent services/runtime | shared hooks | `ScriptExecutor.SetStatus/RunScript`、战斗边界 | 模块 09 | P1 |
| 地图事件与对话 | EventList、Map、Dialogue | `EventScripts`、DialogueFlow*、MapItem API | shared hooks、Journey 仅模式路径 | `MapItem.Init`、`DialogueUI.ChooseOption`、`MapSelectUI` | 模块 10 | P1 |
| Boss、敌人意图与奖励 | Enemy、EnemyCard、Buff、Level | `BossScripts`、BattleReward*、敌人池/意图服务 | BGM arbiter、shared hooks | `BattleRewardsUI.Entry/ModeSetReward`、敌人 Card/Status 流程 | 模块 10 | P1 |
| 卡牌表现与卡框 | visual registry、图片、VisualBundle | CardVisual registries、presentation routers、`Hooks/Visual` | shared scheduler/log/UI | `CardChoiceItem.Initialize`、CardItem 生命周期、`FightUI.UpdateCardItemPos` | 模块 11 | P1 |
| HUD、图标与临时 UI | Buff/Blessing/Enemy 图标、UI 素材 | StarScore/Field HUD、animated icon runtimes、modal/pool/safety | AuraUiShared、raycast safety、transition guard | `BuffItem.Init`、`BlessItem.Init`、`EnemyItem.Init`、`UIManager.CloseUI`、`UIBase.Close` | 模块 11 | P1 |
| 音频、BGM、皮肤、CG | audio/visual registry、SharedResources | `AudioApi`、BGM provider、SkillCg feature、resource preloader | AuraAudio/Cg/Skin、Audio/BGM arbiters、Core package | 战斗/角色上下文、Unity Audio/AssetBundle、共享注册协议 | 模块 12 | P1 |
| 联机 RPC 与状态同步 | 模式、场地、投影、百变、冒险状态 | `SunExpNetworkRuntime`、authority runtime、各 Rpc/NetworkSync | AuraOnline、shared sync/authority conventions | Mirror Command/ClientRpc/TargetRpc、`MapManager` 网络方法 | 模块 13 | P0 |
| 生命周期、性能与兼容 | ModConfig、性能设置 | routers、frame scheduler、resource cache、pool、diagnostics、compat APIs | Core hooks/scheduler/log | 战斗、卡牌、UI 生命周期及反射目标 | 模块 14 | P0 |
| 构建与发布 | csproj、Scripts DLL、VisualBundle、shared manifests | build/test PowerShell、架构与发布门禁 | `Aura.Shared.dll` 全消费者一致性 | `Managed` 编译契约，不属于运行时 Hook | 模块 15 | P0 |

## 4. Aura 共享与核心覆盖

`AuraSharedRuntime-Dev/Aura.Shared.csproj` 当前编入以下组件，正式文档不得遗漏：

| 组件 | 层级 | SunExp 使用面 | 文档归属 |
| --- | --- | --- | --- |
| AuraSharedCore | Aura 核心层 | 初始化、包安装、注册表、Hook、调度、配置 | 共享/核心、性能、构建发布 |
| AuraJourneyShared | 共享领域层 | 日耀回忆 route/state/map projection | 日耀回忆、共享/核心 |
| AuraAudioShared | 共享领域层 | 音频注册、资源解析和播放 | 音频/BGM/CG |
| AudioArbiterShared | 共享领域层 | 音频提供者选择与冲突处理 | 音频/BGM/CG |
| BattleBgmArbiterShared | 共享领域层 | 战斗 BGM 提供与仲裁 | 音频/BGM/CG、Boss |
| AuraCgShared | 共享领域层 | CG 注册、请求、表现同步和去重 | 音频/BGM/CG、网络 |
| AuraSkinShared | 共享领域层 | 皮肤包和角色皮肤注册 | 视觉、共享/核心 |
| StarterDeckArbiterShared | 共享领域层 | 日耀回忆起始卡组 profile | 日耀回忆 |
| AuraOnlineShared | 共享领域层 | 在线状态、同步基础和跨 MOD 协议 | 网络与同步 |
| AuraLogShared | 共享基础 | 日志门控与诊断 | 性能兼容诊断 |
| AuraUiShared | 共享基础 | 可复用 UI 能力 | 视觉/UI |
| UiRaycastSafetyShared | 共享基础 | 临时 UI 销毁和射线清理 | 视觉/UI |
| UiTransitionGuardShared | 共享基础 | 原生 UI 切换恢复 | 视觉/UI、模式准备 |

## 5. 游戏主体与反编译覆盖

### 5.1 必查程序集

| 程序集 | 主要核验内容 | 当前状态 |
| --- | --- | --- |
| `Witch` | 战斗、卡牌、地图、模式、UI、数据管理、脚本执行器 | P0，尚需逐类型建立映射 |
| `Witch.Core` | 事件中心、基础数据/状态/接口和核心协议 | P0，尚需逐类型建立映射 |
| `Mirror` | RPC、sender、序列化和连接语义 | P0，网络章节统一核验 |
| `UnityEngine.*` | UI、Canvas、资源、音频、材质、Shader、对象生命周期 | P1，仅记录 SunExp 直接依赖面 |
| `AllScripts` | 官方数据脚本和脚本调用范式 | P1，作为写法与宿主调用辅助证据 |
| `Assembly-CSharp` | 少量游戏/插件侧行为 | 按实际命中使用，不预设为主程序集 |

### 5.2 已暴露的高价值宿主类型

首批反编译追踪应覆盖：

`ScriptExecutor`、`GameConfigManager`、`Fight_Start`、`FightPlayer`、`CommonCardItem`、`AttackCardItem`、`SkillItem`、`FightUI`、`BattleRewardsUI`、`CardChoiceItem`、`ModeChoiceUI`、`NormalMapManager`、`MapManager`、`MapSelectUI`、`MapItem`、`RoleTable`、`DialogueUI`、`HouseManager`、`GameEntryUI`、`UIManager`、`UIBase`、`BuffItem`、`BlessItem`、`EnemyItem`。

该清单只表示当前代码已经依赖或 Hook 这些类型，不表示其全部方法已经完成反编译确认。

## 6. 交叉检查清单

- [ ] 每个 `SunExp/Data/**/*.csv` 的脚本列均映射到存在的 `Scripting` 公共入口。
- [ ] 每个 `Scripting` 公共入口均归属一个功能模块。
- [ ] 每个 `Mechanics`、`Features`、`Hooks`、`Network` 生产文件均归属至少一个章节。
- [ ] 每个 `AuraSharedHooks.Register*` 或 `[HookBefore/HookAfter]` 目标均进入宿主映射表。
- [ ] 每个反射调用均记录当前签名、旧签名和 fallback。
- [ ] 每个 RPC 均记录方向、真实 sender、权限条件、payload 边界、去重和清理。
- [ ] 每个运行状态均记录 run/fight/player/shared/presentation 作用域。
- [ ] 每个共享注册项均记录 `ownerModId` 和稳定 domain id。
- [ ] 每个资源注册表均记录声明、安装、解析、缓存、播放/展示和卸载链。
- [ ] 每个大型模式均覆盖入口、准备、地图、战斗、奖励、结算、异常退出、旧存档和联机。
- [ ] 每篇正式文档均列出验证脚本和当前已知限制。

## 7. 正文批次状态

| 批次 | 内容 | 状态 |
| --- | --- | --- |
| A | README、整体架构、加载链、C# 分层、Aura 共享/核心、游戏主体接入 | 已完成，2026-07-13 |
| B | 卡牌/Buff/遗物、战斗事件/场地/标签、乌娜、洛奈尔 | 已完成，2026-07-13 |
| C | 日耀回忆、无尽之海、无尽深渊、地图与奖励 | 已完成，2026-07-13 |
| D | 伙伴/使魔、百变、投影、心变、事件/对话/Boss | 待开始 |
| E | 视觉/UI、音频/BGM/皮肤/CG、网络、性能/兼容、构建发布 | 待开始 |
| F | 内容实体清单、宿主映射表、所有权矩阵、术语表和交叉链接 | 待开始 |

## 8. 后续正文条件

批次 B 及后续正文继续遵守：

1. 使用已确认的“Aura 核心层”和“游戏主体层”术语口径。
2. 模块文档采用固定模板，并允许按篇幅拆分大型模块。
3. 旧文档继续保持排除，不作为内容来源。
4. 新发现的功能、宿主接入点或共享组件先回填本矩阵。

正文撰写过程中允许修正矩阵，但任何新增模块、宿主接入点或共享组件都必须先回填本清单。
