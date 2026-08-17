# AuraToolsExp 工具箱设置与模块架构详细设计

## 1. 文档目的

本文定义 AuraToolsExp 从“中央设置脚本管理全部功能”迁移到“注册式工具箱”的目标架构。

设计同时解决四类问题：

- 一级“妙妙工具”页面只承担功能发现、总开关、状态摘要和设置入口。
- 各功能的具体参数由功能自己的二级设置页或运行时界面管理。
- UI 状态变化不得因为列表重建而丢失滚动位置、焦点或当前选择。
- 新增工具功能不再要求同时修改 `Entry.cs`、根配置模型和中央设置页面。

本文是详细设计，不要求第一轮迁移立即拆分程序集，也不改变 AuraToolsExp 与内容 MOD、Aura 共享层之间的既有依赖方向。

## 2. 设计结论

本轮设计固定以下决策：

1. AuraToolsExp 继续发布为一个 `Entry.dll`，暂不按功能拆 DLL。
2. 内置工具模块使用显式注册，不使用程序集反射扫描。
3. `Entry` 只初始化共享基础设施、模块目录和模块宿主，不再逐个了解具体功能。
4. 一级页面不提供功能参数编辑，只显示总开关、用户价值、状态和入口。
5. 每个功能拥有自己的配置、运行时适配器、状态投影和设置页。
6. 配置事件按模块分发，逐步淘汰无差别的全局 `Changed` 通知。
7. 列表更新默认采用稳定 ID 差量更新；必须重建时使用统一的滚动和焦点事务。
8. 通用滚动、焦点、差量列表和 UI 安全能力进入 `AuraUiShared`；功能语义留在 AuraToolsExp。
9. 内容 MOD 继续通过共享领域注册表声明资源和规则，不依赖 `AuraToolsExp.Dll`。

## 3. 目标边界

### 3.1 AuraToolsExp 负责

- 工具模块注册、启停和状态汇总。
- 一级工具箱页面、搜索、分类和二级设置页路由。
- 玩家本地配置、导入、导出、预览、诊断和覆盖。
- 工具自有资源、模型、规则和正式游戏扩展。
- 对共享注册声明的读取与本地有效状态覆盖。

### 3.2 Aura 共享层负责

- Owner-qualified 注册、配置存储、资源包、日志和 RPC 权威。
- CG、Audio、Skin、StarterDeck、Journey、Mode、Online、CombatAI 等领域协议。
- 通用 UI 组件、模态层、滚动状态、焦点恢复、差量列表和射线安全。
- 不包含“开局卡组”“一键美餐”“战斗策略”等 AuraToolsExp 功能语义。

### 3.3 内容 MOD 负责

- 自有内容、资源、触发语义和默认声明。
- 通过共享协议注册资源和机器可读元数据。
- 不引用 AuraToolsExp 的模块接口、设置页或运行时类。

## 4. 目标架构

```mermaid
flowchart TD
    Entry["Entry / Composition Root"] --> Foundation["Shared Foundation Bootstrap"]
    Entry --> Catalog["AuraToolModuleCatalog"]
    Catalog --> Host["AuraToolModuleHost"]
    Host --> ModuleA["Tool Module"]
    Host --> ModuleB["Tool Module"]
    Host --> ModuleN["Tool Module"]
    Host --> StateStore["Module State Store"]
    Host --> ConfigBus["Scoped Config Bus"]
    SettingsHook["SettingUI Adapter"] --> Shell["Toolbox Settings Shell"]
    Shell --> Catalog
    Shell --> StateStore
    Shell --> Router["Settings Page Router"]
    Router --> ModulePage["Module-owned Settings Page"]
    ModuleA --> Shared["Aura.Shared Domain Runtimes"]
    ModuleB --> Shared
```

依赖方向固定为：

```text
Entry -> ModuleHost -> Module contract
SettingsShell -> Module catalog/state/page contract
Concrete module -> Config + Infrastructure + Shared domain runtime
Concrete module settings page -> AuraUiShared
Shared runtime -X-> AuraToolsExp concrete module
Content mod -X-> AuraToolsExp concrete module
```

## 5. 目录设计

建议新增以下目录：

```text
AuraToolsExp-Dev/
├─ Modules/
│  ├─ Contracts/
│  │  ├─ IAuraToolModule.cs
│  │  ├─ IAuraToolSettingsPage.cs
│  │  ├─ AuraToolModuleDescriptor.cs
│  │  ├─ AuraToolModuleState.cs
│  │  └─ AuraToolOperationResult.cs
│  ├─ AuraToolModuleCatalog.cs
│  ├─ AuraToolModuleHost.cs
│  ├─ AuraToolModuleStateStore.cs
│  └─ AuraToolsBuiltInModules.cs
├─ Config/
│  ├─ AuraToolConfigStore.cs
│  ├─ AuraToolConfigChangeBus.cs
│  └─ Migration/
├─ Features/
│  └─ <Feature>/
│     ├─ <Feature>Module.cs
│     ├─ <Feature>SettingsPage.cs
│     ├─ Runtime/
│     ├─ Presentation/
│     └─ Storage/
└─ Features/Settings/
   ├─ AuraToolsSettingsRuntime.cs
   ├─ ToolboxSettingsShell.cs
   ├─ ToolboxCategoryNavigation.cs
   ├─ ToolboxModuleList.cs
   ├─ ToolboxModuleRow.cs
   └─ ToolboxSettingsPageRouter.cs
```

现有功能不需要为了满足目录形式而一次性移动。第一阶段允许 `<Feature>Module.cs` 作为现有静态 Runtime 的薄适配器。

## 6. 模块契约

### 6.1 模块描述

描述符只保存稳定元数据，不保存 Unity 对象或临时状态。

```csharp
public sealed class AuraToolModuleDescriptor
{
    public string ModuleId { get; init; } = "";
    public string CategoryId { get; init; } = "";
    public int Order { get; init; }
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string IconKey { get; init; } = "";
    public IReadOnlyList<string> SearchTerms { get; init; } = Array.Empty<string>();
    public bool HasSettingsPage { get; init; }
    public bool Experimental { get; init; }
    public bool RequiresRestartWhenChanged { get; init; }
}
```

约束：

- `ModuleId` 永不使用显示文本，发布后不得复用给其他语义。
- `CategoryId` 只影响导航，不参与配置路径或资源身份。
- `Description` 从用户收益出发，不描述实现、协议或文件路径。
- 动态数量、故障和当前模式不写进描述符，由状态对象提供。

### 6.2 模块接口

```csharp
public interface IAuraToolModule
{
    AuraToolModuleDescriptor Descriptor { get; }
    void Initialize(AuraToolModuleContext context);
    AuraToolModuleState SnapshotState();
    AuraToolOperationResult SetEnabled(bool enabled);
    void ApplyCurrentConfiguration();
    IAuraToolSettingsPage? CreateSettingsPage();
}
```

第一轮不要求运行期真正卸载程序集。`SetEnabled(false)` 的含义是：

- 立即停止功能行为和展示。
- 释放支持释放的 Hook、订阅、协程和临时 UI。
- 保留持久化数据、资源注册和再次启用所需的轻量基础设施。
- 不删除用户数据，不修改外部 MOD 的注册源。

### 6.3 模块状态

```csharp
public enum AuraToolModuleAvailability
{
    Ready,
    Disabled,
    Unavailable,
    Degraded,
    Busy,
    RestartRequired
}

public sealed class AuraToolModuleState
{
    public string ModuleId { get; init; } = "";
    public long Revision { get; init; }
    public bool ConfiguredEnabled { get; init; }
    public bool EffectiveEnabled { get; init; }
    public AuraToolModuleAvailability Availability { get; init; }
    public string Summary { get; init; } = "";
    public string? Attention { get; init; }
    public int? ItemCount { get; init; }
}
```

`ConfiguredEnabled` 是用户配置；`EffectiveEnabled` 还考虑根级兼容门、运行环境、多人权限、资源可用性和安全降级。一级界面必须展示两者不一致的原因。

### 6.4 设置页接口

```csharp
public interface IAuraToolSettingsPage
{
    string ModuleId { get; }
    void Build(AuraToolSettingsPageContext context);
    void Activate();
    void Deactivate();
    void Dispose();
}
```

设置页自己拥有：

- 参数控件和验证。
- 导入、导出、扫描、预览等操作。
- 页面内状态刷新。
- 自己的滚动锚点和局部列表协调器。

设置壳层不允许引用 `AuraToolsAudioRoleEditor`、`AuraToolsFeastRuntime` 等具体功能类。

## 7. 模块注册与初始化

### 7.1 显式注册

```csharp
public static class AuraToolsBuiltInModules
{
    public static IReadOnlyList<IAuraToolModule> Create()
    {
        return new IAuraToolModule[]
        {
            new SkinToolModule(),
            new BattleBgmToolModule(),
            new CardUseAudioToolModule(),
            new StarterDeckToolModule(),
            // 其余模块
        };
    }
}
```

选择显式注册的原因：

- 初始化顺序可读、可测试。
- 不依赖运行时反射和程序集枚举。
- 重复 ID、缺少分类和依赖环可在启动前一次校验。
- 仍然只修改一个“模块清单”，而不是修改 Entry、配置根和 UI 三个中央点。

### 7.2 模块宿主

`AuraToolModuleHost` 负责：

- 校验模块 ID 唯一性、分类和顺序。
- 使用 `AuraSharedHooks.RunStep` 隔离每个模块初始化。
- 保存模块实例和状态 revision。
- 接收模块状态变化并发布 `ModuleStateChanged(moduleId, revision)`。
- 防止重复初始化和重复注册 Hook。
- 汇总启动诊断，但不包含具体功能策略。

初始化顺序分为三段：

1. Foundation：Shared Core、GameData、Journey、Mode、RPC、Config、Resource、UI Guard。
2. Modules：创建目录并逐模块初始化。
3. Shell：注入 SettingUI，并只订阅 Catalog 与 StateStore。

## 8. 一级信息架构

### 8.1 分类

建议使用以下稳定分类：

| CategoryId | 显示名 | 模块 |
|---|---|---|
| `gameplay` | 游戏体验 | 开局卡组、卡牌刷新、一键美餐、随身保险箱 |
| `presentation` | 表现与资源 | 角色皮肤、战斗 BGM、出牌音效、像素表情、技能 CG、卡牌使用 CG |
| `records` | 对局与记录 | DPT 统计、战斗回放、对局资料库 |
| `multiplayer` | 联机工具 | MOD 配置同步及将来的联机工具 |
| `intelligence` | 智能战斗 | 战斗策略实验室 |
| `system` | 系统与数据 | 文件日志、数据目录、诊断入口 |

一级页面采用分类导航，不再把所有模块放进一个无限增长的长滚动区。默认进入“全部”或最近使用分类；搜索结果可以跨分类显示。

### 8.2 模块粒度

模块粒度以“用户能否独立启停”为判断标准：

- 战斗 BGM 与出牌音效保持两个模块。
- 技能 CG 与卡牌使用 CG 保持两个模块，但统一归入“表现与资源”。
- DPT 统计与战斗回放拆成两个独立模块。
- 对局资料库是管理入口，不作为必须启用的父开关。
- 数据目录是系统动作，不伪装成可启停模块。

这样用户可以只开启 DPT，不必同时开启回放；也可以保留历史资料库，但停止新记录。

### 8.3 一级模块行

每个模块行只包含：

```text
[图标] 功能名称                         [总开关]
       一句话用户收益
       当前状态或需要处理的问题          [设置]
```

约束：

- 高度稳定，不因状态文本或开关变化改变整体布局。
- 状态最多两行；详细错误进入二级页或日志。
- 行本身不作为 Button，避免父 Button 内嵌 Toggle 的 Selectable 冲突。
- 总开关、设置按钮和可选的修复按钮是互相独立的交互目标。
- 没有设置页的简单功能不显示空的“设置”按钮。
- 关闭模块不会从列表移除，也不会收起导致页面跳动。

### 8.4 一级状态投影

各模块推荐状态摘要：

| 模块 | 一级摘要 |
|---|---|
| 角色皮肤 | `已启用 3 个候选皮肤` / `资源缺失` |
| 战斗 BGM | `通用音频` / `按角色配置 4 个` |
| 出牌音效 | `通用音效` / `按角色配置 2 个` |
| 开局卡组 | `全局卡组 11 张` / `按角色配置 3 个` |
| 卡牌刷新 | `战斗奖励可刷新` |
| 像素表情 | `作品 12 · 收藏 5` |
| 一键美餐 | `已配置 8 个角色` |
| 随身保险箱 | `冒险顶部栏显示入口` |
| MOD 配置同步 | `仅房主可发起同步` / `当前不在联机大厅` |
| DPT 统计 | `本场 · 全部阵营 · 表格` |
| 战斗回放 | `自动保存上限 20` |
| 技能 CG | `角色规则 6 条 · 联机同步开启` |
| 卡牌使用 CG | `已启用 4/7 个注册项` |
| 战斗策略实验室 | `未选择模型` / `完整应用 · 模型名称` |
| 文件日志 | `Info 及以上` / `写入失败` |

## 9. 二级设置页归属

| 模块 | 二级设置页负责 |
|---|---|
| 角色皮肤 | 候选管理、当前选择、联机同步、角色选择入口、资源目录 |
| 战斗 BGM | 通用/角色模式、路径、优先级、文件选择、角色覆盖 |
| 出牌音效 | 通用/角色模式、增益、文件选择、角色覆盖、联机行为说明 |
| 开局卡组 | 全局/角色模式、Profile 选择、候选卡包、卡组编辑 |
| 像素表情 | 工坊、作品库、收藏、联机展示设置 |
| 一键美餐 | 角色开关、候选 CG、选择策略、人工资源、预览 |
| DPT 统计 | 展示模式、范围、阵营和历史统计入口 |
| 战斗回放 | 自动记录、保存上限、视频导出、兼容性和资料库 |
| 技能 CG | 角色规则、触发技能、优先级、表现、图片资源、同步 |
| 卡牌使用 CG | 注册项启停、Owner、资源目录和本地覆盖 |
| 战斗策略实验室 | 模型库、运行模式、游戏主体、训练、评估、实机验证和诊断 |
| 文件日志 | 等级、来源、Unity 类型、堆栈、队列、Flush 和文件保留 |

二级设置页可以继续使用 Overlay，但由 `ToolboxSettingsPageRouter` 统一管理打开、返回、销毁、焦点恢复和页面标题。

## 10. 配置设计

### 10.1 配置路径

目标路径：

```text
ModsData/AuraShared/Config/Owners/AuraToolsExp/AuraTools/
├─ shell.json
└─ Modules/
   ├─ presentation.skin.json
   ├─ presentation.battle-bgm.json
   ├─ presentation.card-use-audio.json
   ├─ gameplay.starter-deck.json
   ├─ records.damage-statistics.json
   ├─ records.battle-replay.json
   └─ ...
```

`shell.json` 只保存纯 UI 偏好，例如上次分类和搜索状态；不保存功能开关。

模块配置继续通过 `AuraSharedConfigStore` 读写，保留 owner、system、revision 和 schemaVersion 语义。

### 10.2 泛型配置存储

```csharp
public interface IAuraToolConfigStore<T>
{
    AuraToolConfigSnapshot<T> Read();
    AuraToolConfigWriteResult Write(T value, long expectedRevision);
    event Action<AuraToolConfigSnapshot<T>> Changed;
}
```

模块只订阅自己的配置 Store。共享配置变化通过模块状态 revision 投影到一级页面，而不是触发整个设置页重建。

### 10.3 保存流程

```text
用户切换控件
-> SettingsPage/Module 校验输入
-> 写入模块 ConfigStore
-> 模块 ApplyCurrentConfiguration
-> ModuleStateStore 更新该 moduleId
-> 一级行原地刷新状态
```

禁止以下流程：

```text
用户切换控件
-> 全局 Changed
-> 所有功能重新配置
-> 清空并重建设置页面
```

### 10.4 旧配置迁移

迁移使用读旧、写新、保留旧的兼容策略：

1. 新模块文件不存在时，从当前 `AudioSettings.json`、`MatchExperienceSettings.json` 等读取。
2. Normalize 后写入新的模块文件，并记录 `migratedFromSchema`。
3. 至少两个发布周期继续支持读取旧配置作为 fallback。
4. 迁移不删除旧文件，不重写内容 MOD 注册源。
5. 新旧配置同时存在时，以新模块配置为准。

特殊迁移：

- `matchRecords.statistics` -> `records.damage-statistics`。
- `matchRecords.replay` -> `records.battle-replay`。
- 原 `matchRecords.enabled` 只作为首次迁移时两个模块的父级门，不保留为长期用户开关。
- 原根级 `ModuleFileConfig.Enabled` 在迁移后只用于兼容读取，不继续成为不可见的第二层总开关。

## 11. 滚动与焦点设计

### 11.1 问题模型

`ClearChildren()` 会在同一帧把旧元素隐藏，导致 Content 高度暂时缩小。`ScrollRect` 随后把位置钳制到合法范围，通常是顶部。新元素加入后，只恢复了内容高度，没有恢复用户的视觉锚点。

只保存 `verticalNormalizedPosition` 不足以处理：

- 列表前方增加或删除元素。
- 行高变化。
- 分类、筛选结果变化。
- 当前焦点元素被替换。

### 11.2 共享稳定 ID

在 `AuraUiShared` 增加：

```csharp
public sealed class AuraUiStableId : MonoBehaviour
{
    public string Value { get; private set; } = "";
    public void Set(string value) => Value = value ?? "";
}
```

模块行使用 `moduleId`；候选资源使用 owner-qualified ID；角色行使用稳定 roleId；不得使用显示名或 sibling index。

### 11.3 视图状态快照

```csharp
public sealed class AuraUiViewStateSnapshot
{
    public string? FocusedId { get; init; }
    public string? AnchorId { get; init; }
    public float AnchorOffsetY { get; init; }
    public float NormalizedFallback { get; init; }
}
```

捕获规则：

1. 优先记录 `EventSystem.current.currentSelectedGameObject` 最近的 `AuraUiStableId`。
2. 记录视口顶部第一个仍可见的稳定元素及其相对 Y。
3. 保存 normalized position 作为找不到锚点时的 fallback。

恢复规则：

1. 停止 ScrollRect 惯性。
2. 完成差量更新并在下一帧执行布局。
3. 找到原锚点，恢复相同的视口内相对 Y。
4. 恢复仍存在且可交互的焦点控件。
5. 焦点元素消失时，选择同模块的设置按钮、邻近行或分类导航。
6. 最后才使用 normalized fallback。

### 11.4 差量列表

在 `AuraUiShared` 增加 `AuraUiKeyedListReconciler<TKey, TModel, TView>`，职责为：

- 按稳定 Key 复用现有行。
- 只创建新增项、销毁移除项、更新变化项。
- 保持排序后的 sibling index。
- 在一次 `AuraUiViewMutation` 中自动捕获和恢复视图状态。

复选框切换只调用对应行的 `Update(model)`，不得刷新整个列表。

### 11.5 Toggle 规范

- 初始化一律使用 `SetIsOnWithoutNotify`。
- Toggle 回调不得调用页面级 `Show()` 或 `ClearChildren()`。
- Toggle 与整行 Button 不嵌套。
- 保存失败时恢复原值并显示模块内错误，不改变滚动位置。
- 需要重启的设置显示“重启后生效”，但仍原地更新配置状态。

## 12. 设置壳层状态

`ToolboxSettingsShellState` 保存：

```csharp
public sealed class ToolboxSettingsShellState
{
    public string CategoryId { get; set; } = "all";
    public string SearchText { get; set; } = "";
    public Dictionary<string, AuraUiViewStateSnapshot> ScrollByCategory { get; } = new();
    public string? ActiveModuleId { get; set; }
}
```

生命周期：

- 同一个 SettingUI 实例关闭再打开时恢复分类和滚动位置。
- SettingUI 销毁时释放 Unity 引用，但可保留纯数据的会话状态。
- 进入回放前释放当前二级页和所有 Owned Overlay。
- 从回放恢复时重新注入壳层，不复用已经销毁的 Transform。

## 13. 状态刷新与性能

### 13.1 事件优先

模块状态由事件驱动更新。只有确实没有事件来源的运行进度才允许轮询。

轮询规则：

- 由页面级单一协调器轮询，不为每一行挂一个 `Update()`。
- 仅轮询当前可见分类和当前打开的二级页。
- 普通状态频率不超过 4Hz；进度动画可单独提高。
- 状态文本变化不得触发结构重建。

### 13.2 构建预算

- SettingUI 注入只创建壳层和当前分类。
- 模块行按当前分类分帧创建，单帧预算由共享 FrameScheduler 管理。
- 一级开关操作不得调用 `Canvas.ForceUpdateCanvases()` 或同步重建整个 Content。
- 模型扫描、数据库查询、文件哈希和媒体检查不得在模块行的渲染函数中执行。

### 13.3 状态缓存

`AuraToolModuleStateStore` 保存最后状态和 revision。壳层只在 revision 变化时更新目标行，避免每次打开页面重新扫描模型、资源和数据库。

## 14. 内置模块清单

建议稳定 ID：

```text
gameplay.starter-deck
gameplay.card-refresh
gameplay.feast
gameplay.safe-box
presentation.skin
presentation.battle-bgm
presentation.card-use-audio
presentation.pixel-emoji
presentation.skill-cg
presentation.card-use-cg
records.damage-statistics
records.battle-replay
multiplayer.mod-sync
intelligence.auto-battle
system.file-logging
```

系统动作：

```text
system.open-data-directory
system.open-log-directory
system.reload-tool-config
```

系统动作不实现 `IAuraToolModule`，由系统页以明确命令按钮呈现。

## 15. 跨 MOD 扩展策略

### 15.1 当前阶段

其他 MOD 注册的皮肤、CG、音频、开局卡组等内容继续出现在对应模块的二级管理页，不自动变成新的一级工具模块。

这可以避免“每个资源包都占一个工具卡片”，也保持内容与工具的边界。

### 15.2 可选的未来阶段

如果确实需要第三方工具 MOD 把一个新工具加入 AuraToolsExp 的一级壳层，应在共享层新增 owner-qualified 的 `AuraTooling.Shared` 协议，而不是让第三方引用 `AuraToolsExp.Dll`。

未来协议至少需要：

- `ownerModId + moduleId` 身份。
- 协议版本和兼容范围。
- 描述符与状态 Provider。
- 设置页 Provider 或受限的声明式设置模型。
- 注销句柄、重复注册保护和故障隔离。
- 不允许注册者获得 AuraToolsExp 私有配置或任意修改其他模块。

在没有第二个真实工具消费者之前，不提前实现该公共协议。

## 16. 测试设计

### 16.1 纯行为测试

- 模块 ID 唯一、分类有效、顺序稳定。
- 一个模块初始化失败不影响后续模块。
- `SetEnabled` 幂等，保存失败可回滚。
- ConfigStore 只通知对应模块。
- 旧配置向新模块配置迁移正确。
- DPT 和回放可以独立启停。
- 状态 revision 只在可见状态实际变化时增加。

### 16.2 UI 状态测试

- 切换模块总开关不会改变分类、搜索文本和滚动锚点。
- 切换列表中间项后，该项仍位于原来的视口相对位置。
- 删除当前焦点项时，焦点移动到合理邻近项。
- 分类切换后分别恢复各自滚动位置。
- 打开并关闭二级页后，焦点返回原模块的设置按钮。
- SettingUI 销毁后不保留 Unity 对象引用。

### 16.3 架构门禁

建议增加以下声明式规则：

- `ToolboxSettingsShell` 不得引用具体 `Features.*` 命名空间。
- 具体模块不得直接写其他模块的配置文件。
- Toggle 回调附近禁止页面级 `ClearChildren`，具体行为由测试覆盖。
- AuraToolsExp 继续禁止引用 Terrias 实现和内容语义。
- Shared 层继续禁止引用 AuraToolsExp 产品语义。
- 所有功能 Hook 继续通过 `AuraToolsHookRegistry` 或共享路由注册。

### 16.4 手工验证

1. 在一级页面滚动到中部，连续切换十个模块，不得跳到顶部。
2. 打开每个分类、二级页并返回，确认分类、滚动和焦点恢复。
3. 使用鼠标、键盘和手柄分别操作总开关与设置按钮。
4. 在模型扫描、训练、回放导出和联机状态变化时保持一级页面稳定。
5. 反复打开/关闭 SettingUI 和进入/退出回放，确认没有残留 Overlay 或射线阻塞。
6. 在 16:9、16:10、窗口化和低分辨率下确认模块行文本不溢出。

## 17. 迁移阶段

### 阶段 0：先解决视图状态问题

- 在 `AuraUiShared` 实现 StableId、ViewState 和 MutationScope。
- 将皮肤、一键美餐、卡牌使用 CG 和自动战斗局部刷新改为差量更新或状态事务。
- 禁止设置页 Build 方法修改配置。
- 为现有页面补滚动和焦点回归测试。

### 阶段 1：建立模块目录和新一级壳层

- 添加模块契约、Catalog、Host 和 StateStore。
- 使用薄适配器包装现有静态 Runtime。
- 新壳层从 Catalog 生成分类和模块行。
- 二级设置暂时调用现有 Editor，确保功能行为不变。

### 阶段 2：迁移二级设置页所有权

- 各 Feature 提供自己的 `SettingsPage`。
- 从 `AuraToolsSettingsRuntime` 移除具体功能构建代码。
- 将战斗策略、日志、对局记录、音频等详细参数完全迁入二级页。

### 阶段 3：拆分配置和事件

- 引入模块 ConfigStore 和模块级 ChangeBus。
- 迁移旧配置，逐步移除根级隐藏开关和全局 `Changed`。
- 拆开 DPT 与回放的父级门。

### 阶段 4：收紧架构门禁

- `Entry.cs` 只保留 Foundation + ModuleHost + SettingsShell。
- 中央设置 Runtime 不再引用具体 Feature。
- 添加模块边界、配置归属和 UI 刷新规则。
- 评估是否存在真实需求，再决定是否设计公开的 `AuraTooling.Shared`。

## 18. 文件级改造落点

第一轮实现预计主要触及：

```text
AuraToolsExp-Dev/Entry.cs
AuraToolsExp-Dev/Config/AuraToolsConfigService.cs
AuraToolsExp-Dev/Features/Settings/AuraToolsSettingsRuntime.cs
AuraToolsExp-Dev/Features/Settings/AuraToolsUi.cs
AuraToolsExp-Dev/Features/Settings/AuraToolsPanelBuildState.cs
AuraToolsExp-Dev/Modules/**
AuraUiShared/AuraUiStandardRenderer.cs
AuraUiShared/AuraUiViewState.cs
AuraUiShared/AuraUiKeyedListReconciler.cs
AuraToolsExp-Dev.Tests/**
tools/architecture-boundary-rules.json
```

不应在第一轮顺手改动具体玩法逻辑、模型格式、回放协议、资源身份或多人 RPC 协议。

## 19. 完成标准

架构迁移完成需要同时满足：

- 新增内置工具只需新增模块实现并加入内置模块目录。
- 一级设置壳层不引用任何具体功能 Runtime 或 Editor。
- 一级页面没有可编辑的功能细节参数。
- 所有总开关都能原地更新状态，不改变滚动和焦点。
- 列表刷新不再依赖无保护的 `ClearChildren()` 全量重建。
- 模块配置事件不会唤醒无关模块。
- DPT 与回放可以独立启停。
- 旧用户配置可无损迁移，外部 MOD 注册源不被改写。
- AuraToolsExp 与 Terrias 继续保持兄弟消费者关系。
- 构建、AuraToolsExp 行为测试、共享兼容测试、架构门禁和手工 UI 清单全部通过。

## 20. 第一轮实施建议

建议首先实施阶段 0 与阶段 1，不同时迁移全部配置。

这轮应交付：

1. 共享滚动和焦点保护基础设施。
2. 内部模块契约、Catalog、Host 和 StateStore。
3. 新的分类式一级工具箱页面。
4. 现有功能的薄模块适配器。
5. 现有二级 Editor 的路由接入。
6. 一级开关、滚动、焦点、模块目录和初始化隔离测试。

这样可以先解决用户直接感知的页面结构和跳顶问题，同时把后续配置拆分、功能迁移和公共扩展建立在稳定契约之上。
