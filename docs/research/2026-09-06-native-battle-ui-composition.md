# 原生战斗界面组成与场地视觉边界

日期：2026-09-06。用途：为 Terrias 场地 BUFF 视觉讨论建立原生实现依据。

本轮读取原生反编译代码及对应 Managed 指纹，未修改产品代码、发布 DLL 或运行游戏验收。下文区分代码能够确认的组成关系与尚需 Prefab / 运行时检查的渲染配置。

## 1. 证据版本

使用 `开发参考资料/反编译文件夹v1.0.24831968`，不是仅凭版本号选取。

| 对象 | 验证结果 |
| --- | --- |
| 仓库 `Managed/Witch.dll` 与快照清单 SHA-256 | 一致：`CD829F84F5A52A6B53973C676BF3DF4618A15F479A23BBA35B82E6802C6CDC83` |
| 仓库 `Managed/Witch.Core.dll` 与快照清单 SHA-256 | 一致：`EDE56686110573070F9E062C8EFC51AC9D60E2AFD9B436590A028B72FDC06295` |
| 仓库 `Managed/Assembly-CSharp.dll` 与快照清单 SHA-256 | 一致：`1F9858612E3C38C5590A0E57A904AEEB980D78C25B9A19534365EC09A0EF9BF2` |
| 快照清单与反编译清单中的 `sourceManifestSha256` | 一致 |
| 反编译清单 | 253 个程序集，完整输入，253 成功、0 失败 |

清单：`开发参考资料/Managed快照/v1.0.24831968/managed.manifest.json`、`artifacts/game-reference/1.0.24831968/decompile.manifest.json`。

这证明选用代码与仓库编译依据一致；本轮没有核对截图对应游戏进程的安装目录或运行中 DLL。大部分 Witch 方法有 Rougamo 包装，行为分析读取其 `_0024Rougamo_*` 实现体。

## 2. 进入战斗的调用链

```text
Commands 的战斗命令
  → GameApp.StartFight(level)
  → FightManager.ReadyToInit(level)
  → 服务端等待参与者，RpcFightCheck 汇总角色数据
  → FightManager.Init 的客户端实现
  → ChangeUnit(FightType.Init)
  → FightInit.Init()
      1. 清理旧的未登记战斗对象
      2. UIManager.ShowUI<FightUI>("FightUI")
      3. 暂时隐藏 FightUI，调用 FightUI.Init()
      4. RpcLoadRoles() 生成本地及其他玩家
      5. 初始化牌库；选择战斗 BGM
      6. 调整背景的 sibling 位置，显示 FightUI
      7. EnemyManager.LoadRes() 生成敌人
      8. 应用祝福/遗物/职业能力，初始化技能按钮和初生牌
  → 填充行动队列和 FightUI.StatusList
  → ReadyToStart() 汇合后进入 FightType.Start
  → Fight_Start.Init() 触发开战事件与开战语音，开始行动处理
```

命令路径还会关闭地图 UI 和 EventUI。`GameApp.StartFight()` 本身没有加载一个新的 Unity 战斗 Scene；这里观察到的是背景对象、战斗对象和 UI 预制体的组合。

证据：[Commands.cs:2953](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Commands.cs:2953)、[GameApp.cs:368](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/GameApp.cs:368)、[FightManager.cs:2983](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/FightManager.cs:2983)、[FightManager.cs:3713](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/FightManager.cs:3713)、[FightInit.cs:143](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/FightInit.cs:143)、[Fight_Start.cs:32](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Fight_Start.cs:32)。

## 3. 画面组成与挂载关系

下面是代码明确创建或引用的对象关系，不是完整 Prefab 导出，也不表示最终从后向前的渲染顺序。尖括号表示动态生成的一类对象。

```text
独立实例化的场景对象
├─ GameApp.NowBackground（资源 UI/Scene/{SceneType}）
│  └─ com / SceneInfo
│     └─ 子 SceneItem：视差信息，具体美术节点依 Prefab
├─ <玩家 / 其他玩家>（资源 Model/player）
│  ├─ FightPlayer 或 OtherPlayer、StatusManager
│  └─ body、head、bottom 等角色节点
├─ <敌人>（资源 Model/AncientDragonStatue，由 Enemy.Init 配置）
└─ chest（资源 Model/Chest，进入时创建并隐藏）

Canvas（由 UIManager 查找）
├─ TopBarUI
│  └─ Content
│     ├─ PlayerStatus：头像、生命/护盾、属性、遗物、财富等
│     ├─ PlayerStatusList：多人模式状态区
│     └─ Buttons：菜单与功能入口
├─ FightUI
│  ├─ Process / Tip / Text：当前行动者的回合提示
│  ├─ Left
│  │  ├─ Time / total / val：行动点显示
│  │  ├─ Card / val：抽牌堆入口与数量
│  │  └─ Skill1 或 Skill2/...：职业技能按钮
│  ├─ ClockBoard
│  │  ├─ 结束回合
│  │  ├─ 确定
│  │  ├─ 结束战斗
│  │  ├─ 重开战斗
│  │  └─ 弃牌堆 / val
│  ├─ container：常规手牌 CardContainer
│  ├─ Selectcontainer：选择中的卡牌 CardContainer
│  ├─ CenterCardContainer：出牌展示/退出动画的卡牌
│  ├─ <StatusBarUI>：各单位的状态条
│  │  ├─ Name
│  │  ├─ HpItem：血量、护盾和数字
│  │  └─ BuffBarUI
│  ├─ <ActionContent / content / ActionMsg>：敌人/伙伴意图等
│  ├─ <EffectList>、<selfUI>：单位附属显示节点
│  └─ Terrias_BattleHudHost / Terrias_FieldBuffHud（MOD 新增）
├─ TitleUI、DeckUI 等按需窗口
└─ Tooltip、ProgressBar、Floating Window、伤害飘字等公共 UI

Upper Canvas（由 UIManager 查找）
└─ ModalWindow / InputWindow 等上层交互窗口

effect（由 UIManager 查找的独立特效容器；其父级需查场景资源）
└─ EffectBase.Play() 生成的瞬时战斗特效
```

`UIManager.Awake()` 分别查找 `Canvas`、`Upper Canvas`、`effect`。普通 `ShowUI` 从 `UI/{uiName}` 加载到 `canvasTf`；场景型窗口使用另一条直接实例化路径。`UIBase.Show()` 还会按 `IsUpperUI` 调整父级。上述三个容器的完整祖先关系及渲染参数没有由这些 C# 创建。

证据：[UIManager.cs:1131](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/UIManager.cs:1131)、[UIBase.cs:488](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/UIBase.cs:488)、[FightUI.cs:2683](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/Window/FightUI.cs:2683)、[TopBarUI.cs:1115](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/Window/TopBarUI.cs:1115)、[BattleHudHost.cs:17](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias-Dev/Hooks/Ui/BattleHudHost.cs:17)。

## 4. 对视觉方案有直接影响的原生细节

### 背景是带场景行为的预制体

`GameApp.UpdateBack(SceneType)` 销毁旧 `NowBackground`，再实例化 `UI/Scene/{SceneType}`。`SceneInfo` 包含地面高度、BGM、参考平面和相机边界；`Awake()` 根据 `groundPos` 计算 `ground_y`，并缓存子 `SceneItem`。`LateUpdate()` 约束主相机、发出 `OnCameraMove`，更新各 `SceneItem` 的视差位置。

玩家、敌人和宝箱均读取 `NowBackground/com/SceneInfo.ground_y` 决定摆放高度。`FightUI.ShowTitle()` 还读取背景名称显示地区名。因此场地视觉替换需要明确是替换美术表现还是切换整个场景预制体；后者涉及这些原生依赖。

证据：[GameApp.cs:846](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/GameApp.cs:846)、[SceneInfo.cs:79](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/SceneInfo.cs:79)、[SceneItem.cs:10](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/SceneItem.cs:10)、[FightInit.cs:302](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/FightInit.cs:302)、[EnemyManager.cs:270](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/EnemyManager.cs:270)、[FightUI.cs:2649](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/Window/FightUI.cs:2649)。

### 顶部栏与战斗专属 UI 分开

`TopBarUI` 在开始/继续冒险时由 `GameApp` 创建，并非 `FightUI` 的子窗口。它更新头像、生命/护盾、基础属性、遗物、金钱和真理，以及右上功能按钮。单人和多人使用不同状态区。胜利时调用其 `FightHide()`，实现只调用 `HideDefend()`，没有整体销毁顶部栏。

因此仅改变 `FightUI` 子节点不会自动改变顶部头像和右上菜单。场地 UI 效果应明确覆盖范围。

证据：[GameApp.cs:820](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/GameApp.cs:820)、[TopBarUI.cs:993](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/Window/TopBarUI.cs:993)、[TopBarUI.cs:1230](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/Window/TopBarUI.cs:1230)。

### 血条和意图通过投影跟随场景单位

`StatusManager.Init()` 先通过 UIManager 创建 `StatusBarUI` / `EffectList` 等，再挂到 `FightUI`；`CreateActionContent()` 也将意图容器挂到 `FightUI`。血条用角色 `bottom`，意图用 `head`，附属显示用 `body`，经过 `Camera.main.WorldToScreenPoint()` 和 `PositionUtility.ScreenPointToCanvasPoint()` 定位。

`SetPosition()` 订阅 `OnCameraMove`，`UpdateObjPos()` 重新投影。`FightUI.SetTurn()` 则会平移主相机，所以人物位置、景深和 HUD 跟随是联动的。不能把截图上的屏幕坐标当成永久锚点。

`StatusBarUI` 创建 `HpItem` 和 `BuffBarUI`；血量填充与护盾使用 `SpriteRenderer` 的材质 `_FillAmount`，文字使用 TMP。

证据：[StatusManager.cs:3824](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/StatusManager.cs:3824)、[StatusManager.cs:3905](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/StatusManager.cs:3905)、[StatusManager.cs:3950](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/StatusManager.cs:3950)、[StatusBarUI.cs:215](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/Window/StatusBarUI.cs:215)、[FightUI.cs:2309](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/Window/FightUI.cs:2309)。

### 手牌有多种渲染组件和独立排序

`FightUI.CreateCardItemInternal()` 将 `UI/CardItem` 实例化到 `container`，再依据卡片脚本初始化。`ICard.SetCardStyle()` 明确按 `Front/background` 是否存在 `MeshRenderer` 选择 Mesh 或 Image 样式路径。Mesh 路径分别设置卡图、框架、镶嵌图像；标题、说明、费用又有各自节点。

`CardItem.SetIndex()` 同时设置 sibling index 与 `SortingGroup.sortingOrder`。悬停进入/退出动画也改变两种排序。出牌展示会在 `CenterCardContainer` 生成另一个卡牌对象，弃牌/焚毁动画还有自己的轨迹和材质处理。

场地光影如果直接作用于卡面，需覆盖这些实际表面与生命周期；“卡牌挂在 Canvas 下”不能证明普通 UI Image 遮罩能按预期覆盖每个表面。

证据：[FightUI.cs:3004](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/Window/FightUI.cs:3004)、[ICard.cs:32](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/ICard.cs:32)、[CardItem.cs:1425](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/CardItem.cs:1425)、[CardAnimationController.cs:229](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/CardAnimationController.cs:229)、[FightUI.cs:3969](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/Window/FightUI.cs:3969)。

### 原生特效与后处理已有用途

`EffectManager` 加载 `Configs/Effects` 中的定义。基础 `EffectBase.Play()` 将特效实例化到 `UIManager.effectContent`，按单位 `head`、`bottom` 或 `center` 放置，并在 `duration` 后销毁。这条基础路径对应瞬时表现，持续场地需要另外绑定场地存续状态。

`TopBarUI.ChangeSan()` 在单人状态区使用 `Material/PostProcess/ScreenLight` 的 `_Enabled` 表达低于最大生命 20% 的状态。`FightUI` 的行动演出会开启 `Material/PostProcess/Blur` 的 `_BLUR_ON`，恢复和销毁路径会关闭它。还有通过主相机移动实现的回合平移与震屏。

这些是已有状态的拥有者；场地不能未经协调长期占用同一材质开关。代码没有证明后处理位于哪次相机绘制前后，也没有证明其是否覆盖全部 UI。

证据：[EffectBase.cs:78](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/EffectBase.cs:78)、[EffectManager.cs:156](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/EffectManager.cs:156)、[TopBarUI.cs:1238](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/Window/TopBarUI.cs:1238)、[FightUI.cs:3911](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/Window/FightUI.cs:3911)。

## 5. 战斗结束与 UI 销毁是不同节点

`Fight_Win.ResetStates()` 清理战斗事件、卡牌运行状态和行动队列，更新顶部栏。普通胜利路径调用 `FightUI.ShowChest()`，保留 FightUI 进入宝箱阶段；特殊关卡分支才直接关闭 FightUI。

`ShowChest()` 显示进入战斗时已创建的宝箱，淡出左侧操作区，并安排稍后显示奖励。`ShowBattleReward()` 打开 `BattleRewardsUI` 后关闭 FightUI。`FightUI.OnDestroy()` 才清理剩余卡牌、状态对象、宝箱、输入监听、模糊和相机位置。

因此“场地 BUFF 只在战斗有效”的清理应绑定战斗语义结束，同时保留界面销毁的兜底；仅监听 FightUI 销毁会使效果延续到宝箱阶段。

证据：[Fight_Win.cs:172](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Fight_Win.cs:172)、[FightUI.cs:2343](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/Window/FightUI.cs:2343)、[FightUI.cs:3270](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/Window/FightUI.cs:3270)、[FightUI.cs:2169](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/Witch/UI/Window/FightUI.cs:2169)。

## 6. 本轮结论与后续确认范围

本轮可以确定场地表现至少涉及三类目标：背景场景的美术节点、以世界坐标定位的环境/角色效果、Canvas 内的战斗操作和信息组件。它们可以共同跟随一个场地状态，但需要分别选择表现位置。

前一轮“分层更换远景、保留平台”的建议应保留为美术方向。是否能直接替换对应节点，必须读取该场景 Prefab；当前代码只证明背景系统支持场景信息和子项视差，没有证明截图背景恰好被拆成哪些图层。

在选定实现方式前，需进一步查看：

- 截图对应 `UI/Scene/{SceneType}` 的真实子节点、Renderer、材质、灯光和地面标记。
- `Canvas` / `Upper Canvas` 的 `renderMode`、`worldCamera`、排序配置及相机堆叠。
- FightUI、CardItem、HpItem 等 Prefab 中实际序列化的 Canvas / Renderer / SortingGroup 参数。
- 渲染器配置中的后处理注入点、目标图层以及灯光影响范围。
- 原生场景与启用 Terrias / AuraTools 后的实际对象树是否有差异。

这轮不把 Canvas 名称、Transform sibling index 或 C# 类型名称当成最终绘制次序的证明，也不为截图中未在代码中定位的装饰和按钮猜测节点名。
