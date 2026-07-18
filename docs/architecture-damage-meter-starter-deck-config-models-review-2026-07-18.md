# DamageMeter、StarterDeck 与 ConfigModels 架构评审（2026-07-18）

> 评审日期：2026-07-18
>
> 代码基线：`7c353603 拆分 SolarMemory 地图、结算与牌组运行时边界`
>
> 范围：AuraToolsExp 的 DamageMeter、StarterDeck 与配置 DTO；不包含 BattleBgm，也不修改运行时代码

## 1. 评审结论

旧评审中“三个模块都仍是大型单文件”的判断已经不再准确。当前状态应修正为：

| 模块 | 当前状态 | 主要结论 | 建议优先级 |
| --- | --- | --- | --- |
| DamageMeter | 已有 Model、Network、Resolution、Capture、Input、SettlementCg 分层，但主 Runtime 仍为 2454 行，UI 1640 行，Network Runtime 1023 行 | 已完成外围领域化，尚未完成 Hook/capture、结算历史、UI 展示和网络会话的所有权收口 | 高 |
| StarterDeck | 共享 Arbiter 已存在，AuraTools 侧只拆出 133 行分类策略；2279 行文件仍同时包含 Runtime、Catalog、Resolver、Editor、RoleManager | 是当前最清晰、最容易按稳定类型边界拆分的单文件模块 | 高 |
| ConfigModels | 原 1254 行根文件已拆分；`AuraToolsConfigModels.cs` 仅 52 行 | 原评审目标已经完成，不应继续把根文件列为复杂 Runtime；剩余问题是 442 行 MatchExperience DTO 的二级物理归档 | 低 |

因此，后续不应把三者作为同等规模、同等风险的任务处理。建议先迁移测试护栏，再完成 DamageMeter 与 StarterDeck；ConfigModels 只做低风险收尾。

## 2. 当前实际规模

### 2.1 DamageMeter

`AuraToolsExp-Dev/Features/DamageMeter` 当前共有 29 个 C# 文件、约 11544 行。最大文件为：

| 文件 | 行数 | 当前职责 |
| --- | ---: | --- |
| `AuraToolsDamageMeterRuntime.cs` | 2454 | 初始化、Hook 注册、战斗生命周期、UI 可用性、结算归档、队伍和头像采集、捕获帧配对、Buff 归因桥接、事件提交 |
| `AuraToolsDamageMeterUi.cs` | 1640 | HUD、历史窗口、局外历史、详情窗口、头像解码、UI 工厂、拖拽和 Driver |
| `DamageMeterNetworkRuntime.cs` | 1023 | Ledger 所有权、提交批处理、服务器验证、控制状态、快照压缩、响应预算、发送与会话状态 |
| `BuffAttributionEngine.cs` | 1016 | Buff 归因事务和状态槽 |
| `DamageSettlementCgAssetCache.cs` | 648 | 结算 CG 资源缓存 |
| `DamageMeterFightIndex.cs` | 589 | 战斗对象和队伍身份索引 |
| `DamageSettlementCgRuntime.cs` | 564 | 结算 CG 生命周期与播放 |

这说明 DamageMeter 并非“没有拆分”，而是外围模块已经形成，中心协调器仍然持有多个独立生命周期状态。

### 2.2 StarterDeck

`AuraToolsExp-Dev/Features/StarterDeck` 只有两个文件，共约 2412 行：

| 文件 | 行数 | 当前职责 |
| --- | ---: | --- |
| `AuraToolsStarterDeckRuntime.cs` | 2279 | Hook、牌组应用、联机角色判断、Profile 解析、本地配置、卡牌目录、卡图缓存、编辑器、角色管理器 |
| `StarterDeckCardClassification.cs` | 133 | 生涯技能卡和衍生卡分类 |

主文件内同时声明：

- `AuraToolsStarterDeckRuntime`；
- `StarterDeckResolvedProfile`；
- `StarterDeckCardPackGroup`；
- `StarterDeckCardCatalogSnapshot`；
- `StarterDeckCardCatalogEntry`；
- `StarterDeckRuntimeRole`；
- `AuraToolsStarterDeckEditor`；
- `AuraToolsStarterDeckRoleManager`。

共享的 `StarterDeckArbiterRuntime` 已经负责 Profile 合法性、优先级、所有权声明和牌组应用协议。AuraTools 后续拆分应继续作为该共享协议的工具侧消费者，不能把工具 UI、本地配置或宿主扫描逻辑移入 Shared。

### 2.3 ConfigModels

配置目录当前共有 7 个 C# 文件、约 1788 行：

| 文件 | 行数 | 状态 |
| --- | ---: | --- |
| `AuraToolsConfigModels.cs` | 52 | 只保留根配置索引和模块文件引用，目标已完成 |
| `AuraToolsAudioSettings.cs` | 184 | Audio/BGM/CardUse DTO |
| `AuraToolsLoggingSettings.cs` | 212 | 日志 DTO |
| `AuraToolsSkillCgSettings.cs` | 371 | Skill CG DTO |
| `AuraToolsSkinSettings.cs` | 32 | Skin DTO |
| `AuraToolsMatchExperienceSettings.cs` | 442 | StarterDeck、SafeBox、ModSync、Feast、DamageMeter、CardRefresh DTO |
| `AuraToolsConfigService.cs` | 495 | 配置加载、保存和 Skill CG 注册项导入；不属于 ConfigModels 本轮范围 |

提交 `6030820b 拆分 AuraTools 配置模型并完善兼容护栏` 已经完成原 `ConfigModels` 单体拆分。因此后续只需决定是否把 `AuraToolsMatchExperienceSettings.cs` 内的功能 DTO 再按文件归档，不能把它描述为原方案尚未启动。

## 3. DamageMeter 详细评审

### 3.1 已经有效的边界

以下拆分已经形成，应继续保留：

- Model：单场 Ledger、跨场 RunAggregate、历史记录、局外历史、格式化与 MVP 计算；
- Network：RPC command、authority policy、持久化和 Network Runtime；
- Resolution：FightIndex、BuffAttributionEngine 和 resolver facade；
- Capture：有容量和对象池约束的 `DamageFrameWindow<T>`；
- Input：热键解析与 Unity 输入适配；
- SettlementCg：Payload、动画规格、资源缓存、空闲解析和播放 Runtime；
- PerformanceCounters：Hook、UI、网络和归因的聚合诊断。

纯模型已经有行为测试，RPC sender authority、快照顺序、长期累计、历史记录、结算 CG payload 和动画规格也已有覆盖。

### 3.2 主 Runtime 的剩余混合职责

`AuraToolsDamageMeterRuntime` 仍同时拥有四组独立状态：

1. Hook 注册和启停：`HookRegistrations`、`hooksRegistered`、配置变更和共享战斗生命周期订阅；
2. UI 协调：`Visible`、`Available`、`uiDirty`、刷新节流、失败熔断和准备/冒险界面显隐；
3. 冒险结算：历史恢复、队伍快照、头像 PNG 缓存、模式识别和结算归档；
4. 伤害捕获：Hit/PureHp/HpSetter/Buff/StatusBuff 五类帧窗口、目标列表对象池、反射访问器、Buff 广播监听和事件提交。

这些状态具有不同清理时机：Hook 启停、战斗开始/结束、冒险开始/结算、UI 打开/关闭。继续由同一个静态类型拥有，会使任何新增 Hook、结算字段或 UI 状态都扩大同一失败半径。

### 3.3 目标边界

建议最终形成以下结构：

| 边界 | 所有权 |
| --- | --- |
| `AuraToolsDamageMeterRuntime` | 公共 Facade；初始化子组件，暴露 Enabled/Visible/Available、历史入口和兼容调用 |
| `DamageMeterHookAdapter` | 注册/释放 Hook，将 `ModHookContext` 映射为捕获或生命周期调用；不拥有 Ledger 和历史 |
| `DamageCaptureSession` | 五类帧窗口、对象池、调用序列、帧淘汰、反射访问器缓存和 Reset；战斗为唯一生命周期 |
| `DamageCaptureCoordinator` | 处理 Hit/PureChangeHp/CurHp/Buff 观察，调用 FightIndex、BuffAttribution 和事件工厂 |
| `DamageEventFactory` | 规范化归因结果、字符串与数值预算，构造 `DamageEvent`；不依赖 Hook |
| `DamageMeterLifecycleCoordinator` | 战斗开始、回合、结束，协调 Capture、FightIndex、Network 和 UI dirty 状态 |
| `DamageMeterSettlementRuntime` | 冒险历史恢复、结算判定、队伍快照、头像缓存和归档 |
| `DamageMeterAvailabilityRuntime` | 游戏入口/准备/地图/战斗界面的可用性与显示状态 |

`DamageMeterNetworkRuntime` 的后续内部拆分可作为第二阶段：

- `DamageMeterSubmissionBatcher`：本地提交队列和节流；
- `DamageMeterSnapshotCompactor`：纯快照预算与裁剪；
- `DamageMeterNetworkSession`：session、sequence、rate window 和控制状态；
- Network Runtime 保留 RPC 调度和 Ledger Facade。

该网络逻辑是 DamageMeter 领域协议，仍属于 AuraToolsExp。只有通用的 sender binding、payload guard 和分块传输基础应留在 Shared；不要把 DamageMeter 的统计语义提升到 AuraSharedCore。

### 3.4 UI 边界

`AuraToolsDamageMeterUi` 虽然已经从主 Runtime 分离，但仍把四种变化原因放在一个文件中：

- 浮动 HUD 和行池；
- 当前战斗详情；
- 场内历史；
- 局外历史和头像解码；
- Sprite/九宫格/UI 工厂；
- 拖拽组件与 Driver。

建议在捕获和结算边界稳定后再拆为 `DamageMeterHudPresenter`、`DamageMeterDetailsPresenter`、`DamageHistoryPresenter`、`OutOfRunHistoryPresenter` 和 `DamageMeterUiAssets`。不要在同一批次同时修改捕获算法与 Unity UI。

### 3.5 测试缺口

当前 `AuraToolsExp-Dev.Tests/Program.cs` 有 22 处直接针对 `damageMeterRuntime.Contains(...)` 的固定文件检查。最关键的缺口是：

- Hit 与 DamageText 的配对；
- PureChangeHp 多目标帧配对；
- `set_CurHp` 与已有 PureHp 帧的重复抑制；
- Script/Status AddBuff 前后帧配对；
- 四帧过期清理和对象池归还；
- 捕获 Reset 后不得保留旧战斗归因；
- DamageEvent 字符串和数值预算；
- 结算历史重复归档抑制；
- 头像缓存条目、像素和 PNG 字节上限。

拆分前应把上述结果改成可执行行为测试；文件字符串检查只保留 Hook 方向、共享路由使用、禁止内容 MOD 语义和禁止越权发送等架构约束。

## 4. StarterDeck 详细评审

### 4.1 当前最稳定的拆分点

`AuraToolsStarterDeckEditor` 和 `AuraToolsStarterDeckRoleManager` 已经是独立顶层类型，却仍放在 Runtime 文件中。这两类可首先物理移动，保持命名空间、类型名和公开方法不变，风险最低。

其余职责可以按以下边界继续拆分：

| 边界 | 所有权 |
| --- | --- |
| `AuraToolsStarterDeckRuntime` | 初始化、Hook 编排和对外 Facade |
| `StarterDeckApplicationCoordinator` | World Simulation 判断、本地 RoleTable 所有权、外部模式/Arbiter 所有权检查、ApplyDeck 和应用元数据 |
| `StarterDeckProfileResolver` | 注册 Profile、本地全局/角色 Profile、显式选择、角色 MOD 优先级与有效 Profile 解析 |
| `StarterDeckLocalProfileStore` | 本地 Profile 新建、删除、选择和保存；只操作 AuraTools 配置 |
| `StarterDeckCardCatalog` | 宿主 Card/CardPack/Career 扫描、快照缓存、分组、隐藏/技能/系统技能分类和失效/预热 |
| `StarterDeckCardPresentation` | 显示名、排序键、稀有度、费用和卡图缓存 |
| `AuraToolsStarterDeckEditor` | 单个全局或角色本地 Profile 编辑窗口 |
| `AuraToolsStarterDeckRoleManager` | 角色列表、有效 Profile 展示和 Profile picker |

`StarterDeckCardClassification` 继续保持纯策略，不应重新吸收宿主表扫描或 SunExp 卡牌 ID。

### 4.2 共享边界

必须保持以下依赖方向：

```text
AuraTools UI / local config / host adapter
                  ↓
      StarterDeckArbiter.Shared
                  ↑
       content mod profile manifests
```

- Shared 负责 owner-qualified Profile、合法性、排序、冲突和应用协议；
- AuraTools 负责本地编辑、覆盖选择、宿主目录扫描和工具界面；
- SunExp 等内容 MOD 只注册自己拥有的 Profile；
- AuraTools 不读取 SunExp 私有目录，也不硬编码 SolarMemory 或具体内容卡牌 ID。

### 4.3 测试耦合

当前 AuraTools 测试有 16 处 `starterDeckRuntime.Contains(...)`，`Test-MainSharedFramework.ps1` 还有 33 处固定读取同一 Runtime 文件的检查。这些检查同时覆盖 Hook、catalog、resolver 和 UI，导致即使只移动已有顶层类型也会失败。

建议拆分测试：

1. Shared 行为测试：Profile eligibility、显式选择、本地角色、角色 MOD 推荐、全局回退、验证结果和稳定排序；
2. AuraTools 纯行为测试：本地配置迁移、Profile 选择持久化、Deck 构建、Catalog 分组和失效；
3. Adapter 源码护栏：`GameEntryUI.StartGame` 和 `PlayerManager.CmdSyncRoleTable` 仍为 Before，禁止 `NormalMapManager.InitRoleTable`，RoleTable 必须属于本地玩家；
4. UI 源码护栏：检查 Editor/RoleManager 各自的新文件，而不是要求所有类型位于 Runtime；
5. content/tool/shared 护栏：禁止 `SolarMemory`、SunExp 私有 ID 和直接修改外国注册 Profile。

### 4.4 不建议的做法

- 不用 `partial AuraToolsStarterDeckRuntime` 把同一静态状态机械分散到多个文件；
- 不把 Editor 和 RoleManager 迁入 Shared；
- 不让 CardCatalog 在 ModInitialize 早期扫描尚未完成注册的宿主表；
- 不改变 `GameEntryUI.StartGame`、`CmdSyncRoleTable` 的应用时机；
- 不在源码拆分批次顺便改变 Global/RoleSpecific 或 Profile precedence。

## 5. ConfigModels 重新评审

### 5.1 已完成项

以下旧问题已经关闭：

- 根配置不再包含 Audio、SkillCg、MatchExperience、Skin、Logging 的全部 DTO；
- 五个配置 JSON 已有对应领域 Settings 文件；
- 根文件只负责模块文件名、启用状态和缺省恢复；
- 测试工程已通过 `AuraTools*Settings.cs` 编译领域模型；
- JSON 字段、类型名、默认值和 Normalize 规则已有序列化兼容检查。

所以 `AuraToolsConfigModels.cs` 不再需要重构。

### 5.2 可选的二级拆分

`AuraToolsMatchExperienceSettings.cs` 仍同时包含六个功能域 DTO。建议只做物理归档，保持 `MatchExperienceSettings.json` 和 `AuraToolsMatchExperienceSettings` 聚合根不变：

- `AuraToolsStarterDeckSettings.cs`；
- `AuraToolsDamageMeterSettings.cs`；
- `AuraToolsFeastSettings.cs`；
- `AuraToolsSafeBoxSettings.cs`；
- `AuraToolsModSyncSettings.cs`；
- `AuraToolsCardRefreshSettings.cs`。

文件名使用 `AuraTools*Settings.cs` 可继续被当前测试工程通配符包含。公开类型名、命名空间、`JsonProperty`、schemaVersion=7、默认值和 Normalize 顺序全部保持不变。

### 5.3 明确不在本轮处理

`AuraToolsConfigService.cs` 的 Skill CG 注册项导入约占该服务一半，但它是配置服务与共享注册表的适配问题，不是 ConfigModels DTO 问题。若以后拆分，应单独形成 `SkillCgRegisteredDefaultsImporter`，并保持注册默认、工具内置默认和本地持久化覆盖的优先级；不要混入本次 DTO 文件整理。

## 6. 建议开发路线

### 阶段 0：护栏迁移

1. 为 DamageCapture frame 配对、淘汰、Reset 和事件预算补纯行为测试；
2. 为 StarterDeck Profile resolution、本地选择、Catalog 分组和 Deck 构建补纯行为测试；
3. 将 `Test-MainSharedFramework.ps1` 从单一 Runtime 文本改为按职责读取新边界，或扫描模块目录；
4. 固定公共类型、JSON 字段、RPC command、Profile identity 和 Hook 时序基线。

### 阶段 1：DamageMeter 非热路径拆分

1. 提取 `DamageMeterSettlementRuntime`；
2. 提取队伍/头像采集及缓存；
3. 提取 `DamageMeterAvailabilityRuntime`；
4. 主 Runtime 保持现有公共入口和 UI/Network Facade。

这一阶段不修改 damage capture、Buff attribution 或 RPC 协议。

### 阶段 2：DamageMeter capture 边界

1. 提取 `DamageCaptureSession` 和全部 frame 类型/对象池；
2. 提取 Hook Adapter 与 Context 映射；
3. 提取 `DamageEventFactory`；
4. 将战斗生命周期统一交给 `DamageMeterLifecycleCoordinator`；
5. 保留性能计数、四帧淘汰、诊断 Hook 开关和广播监听清理。

### 阶段 3：DamageMeter UI 与 Network 收尾

1. 先拆详情/历史/局外历史 Presenter，再拆 HUD shell；
2. 视测试收益决定是否拆 Network submission、snapshot compaction 和 session state；
3. 不改变 RPC DTO、authority policy、sequence、payload budget 或历史格式。

### 阶段 4：StarterDeck 物理与职责拆分

1. 先移动 Editor、RoleManager 和其 session/view 类型；
2. 提取 CardCatalog 与 CardPresentation；
3. 提取 LocalProfileStore 与 ProfileResolver；
4. 提取 ApplicationCoordinator 和 Hook Adapter；
5. 将 Runtime 收缩为初始化与兼容 Facade。

### 阶段 5：ConfigModels 收尾

1. 将 MatchExperience 的六组嵌套 DTO 移到独立 `AuraTools*Settings.cs`；
2. 保持同一 JSON 文件和 schemaVersion；
3. 执行序列化 round-trip、空值恢复和旧 schema 迁移测试；
4. 不修改 ConfigService 或共享配置优先级。

## 7. 每轮验收条件

每个开发批次都必须满足：

1. AuraToolsExp 与三个主消费者 Release 构建通过；
2. AuraToolsExp 行为测试、content/tool/shared 边界和 Network RPC authority 检查通过；
3. StarterDeckArbiter 公共 API、Profile identity 和应用时序不变；
4. DamageMeter RPC DTO、sender binding、sequence、snapshot budget 和历史格式不变；
5. `AuraToolsDamageMeterRuntime` 与 `AuraToolsStarterDeckRuntime` 最终只负责初始化、编排和兼容委派；
6. Capture、Resolver、Catalog、Policy 等纯逻辑不依赖 Unity UI 或 `ModHookContext`；
7. 所有缓存和会话状态都有唯一 owner 和明确的 Reset/Dispose 时机；
8. 性能计数、容量限制和错误诊断不因迁移丢失；
9. 配置类型名、命名空间、JSON 字段和默认值保持兼容；
10. 所有打包 `Aura.Shared.dll` 与构建产物哈希一致。

## 8. 风险排序

从高到低：

1. DamageMeter capture：Hook 前后顺序、帧配对和重复抑制错误会直接改变统计结果；
2. DamageMeter network：拆分不当可能影响 authority、sequence、批处理和 payload budget；
3. StarterDeck application：联机本地 RoleTable 判断和应用时机不可改变；
4. StarterDeck catalog：注册完成时机、缓存失效和隐藏/技能卡分类需要保持；
5. DamageMeter UI：主要风险是 Unity 对象、监听器和 raycast 生命周期；
6. ConfigModels 二级物理拆分：只要序列化契约不变，风险最低。

## 9. 最终建议

下一轮不要直接按行数移动 DamageMeter capture。应先完成阶段 0，然后从 DamageMeter 的结算/历史/头像边界开始，因为该区域职责完整、不会改变热路径归因算法。DamageMeter 主 Runtime 稳定收缩后，再处理 capture。

StarterDeck 可以在同一总方案中紧随其后：Editor 和 RoleManager 已是独立顶层类型，先物理迁移可快速解除单文件耦合，再逐步提取 Catalog、Resolver 和 Application。ConfigModels 原目标已经完成，只需作为最后一轮低风险收尾，不应继续占据主重构优先级。

## 10. 本次评审验证

本次只新增评审文档，没有修改运行时代码或构建产物。评审完成后已通过：

- AuraToolsExp：640 项断言；
- Content/Tool/Shared 边界检查；
- `git diff --check`。
