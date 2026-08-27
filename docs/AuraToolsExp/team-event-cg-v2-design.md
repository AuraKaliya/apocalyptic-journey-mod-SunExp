# AuraToolsExp 队伍型事件 CG v2 设计

> 状态：已实现的最终运行合同
> 日期：2026-08-27  
> 适用范围：战斗开场、普通胜利、点金手胜利、仪式胜利、诅咒胜利、战斗失败、冒险结算

交互构图原型位于 [`tools/EventCgScenePrototype/index.html`](../../tools/EventCgScenePrototype/index.html)。它只用于确认有限设置窗口中的信息层级、1 至 8 人构图和资源归一化，不是第二套产品渲染器。运行 `tools/Preview-EventCgSceneV2.ps1` 可复现 17 组 Playwright 快照与几何检查。

## 1. 最终体验合同

队伍型事件 CG 必须首先是一张可读的群像，而不是把若干角色图片塞进固定网格。

1. 一至八名实际冒险参与者都必须可见；不得用伤害排名决定角色是否出现。
2. 默认保持全员等权。只有事件本身存在明确触发者时，才允许使用叙事焦点位。
3. 开场、胜利、失败和结算使用不同的场景语法；三种特殊胜利在胜利语法上叠加独立主题。
4. 任意角色、皮肤、非人形角色和透明边距异常资源都必须经过可见边界归一化。
5. 配置页预览和实际全屏播放必须复用同一个规划器、资源解析器和渲染器。
6. 玩家只选择可选背景叠层、展示时长和资源；默认构图由 `team-tableau.v2` 决定，
   不直接编辑座位坐标、ZIndex 或裁切数字。
7. 缺失专用姿态时按“场景姿态 -> Idle -> 静态首帧 -> 带角色名占位”确定性降级。
8. 联机继续只传处理后的逻辑计划，不传本地路径、纹理、Alpha 边界或图片内容。

## 2. 不变边界

| 所有者 | 职责 |
|---|---|
| AuraToolsExp | 事件语义、默认场景、玩家本地覆盖、设置页与预览入口 |
| 内容 MOD | owner-qualified 角色姿态、皮肤和可选构图元数据声明 |
| AuraCgShared | 场景规划、姿态回退、资源缓存、统一渲染、动画、网络计划与清理 |
| 权威主机 | 冻结队伍顺序、选择场景配置、生成处理后计划并发送 |
| 接收端 | 校验计划，按逻辑资源 ID 解析本地资产并展示，不重算事件原因 |

不得重新引入 DamageMeter 排名依赖、AuraToolsExp 私有播放器、第二套预览渲染器或内容 MOD 主动注册代码。

## 3. 场景语法

| 场景 | 构图族 | 姿态通道 | 动作预设 | 默认时长 | 叙事要求 |
|---|---|---|---|---:|---|
| 战斗开场 | `roster-reveal` | `opening` | `reveal-quick` | 2.2s | 快速介绍队伍，不使用排名或庆祝符号 |
| 普通胜利 | `adaptive-tableau` | `victory` | `celebrate-soft` | 3.0s | 全员等权、清晰、明亮 |
| 点金手胜利 | `adaptive-tableau` | `victory` | `celebrate-gold` | 3.0s | 金色粒子与财富符号，不改变队伍排序 |
| 仪式胜利 | `adaptive-tableau` | `victory` | `celebrate-ritual` | 3.2s | 阵式、环形纹样与受控脉冲 |
| 诅咒胜利 | `adaptive-tableau` | `victory` | `celebrate-curse` | 3.2s | 蝶影、暗色前景与高对比轮廓，避免高频闪烁 |
| 战斗失败 | `quiet-tableau` | `defeat` | `settle-low` | 2.6s | 低位、收敛、无领奖台与庆祝粒子 |
| 冒险结算 | `journey-photo` | `settlement` | `archive-calm` | 4.0s | 旅途合照或档案页，全员等权且便于停留观看 |

默认美术不依赖整张底图。共享组件渲染器根据场景 profile 生成渐变底色、主题洗色、
页眉/页脚线、舞台光带、角色背板与落脚线；普通胜利、点金、仪式、诅咒、开场、失败和
结算使用独立色彩令牌。玩家图片仅作为可选背景叠层，缺失或加载失败时直接回到程序主题。

## 4. 人数构图

| 人数 | 默认布局 | 约束 |
|---:|---|---|
| 1 | 单人主视觉 | 可见高度约为画面 72%，保留事件标题留白 |
| 2 | 向内双人 | 等高、对称、角色朝向中心 |
| 3 | 中心三角 | 中间角色略前但不显示排名，不使用 `1/2/3` 台阶 |
| 4 | 等权弧形 | 四人可见面积差异不超过 12% |
| 5 | `3+2` 错层 | 后排出现在前排肩部间隙，不被固定遮挡 |
| 6 | `3+3` 错层 | 两排中心交错，后排动作幅度下降 |
| 7 | `4+3` 或面板 | 宽体/非人形资源导致遮挡超限时切换肖像面板 |
| 8 | `4+4` 或面板 | 角色最小可见高度不低于画面 30% |

布局选择不是随机分支。主机对候选模板计算固定评分并选最高分：

```text
score = safeFrameRetention
      + visibleAreaBalance
      + horizontalBalance
      + groundAlignment
      - overlapPenalty
      - boundaryCutPenalty
      - titleOcclusionPenalty
```

相同队伍、场景、画面比例和资源元数据必须得到相同布局。评分相同时按稳定布局 ID 排序。

## 5. 数据合同

### 5.1 SceneProfile

`SceneProfile` 属于 AuraToolsExp 的事件配置投影；共享层只理解其中的通用视觉字段。

```json
{
  "sceneId": "victory.standard",
  "layoutProfileId": "AuraToolsExp:team-tableau.v2",
  "poseChannel": "victory",
  "motionProfileId": "AuraToolsExp:celebrate-soft.v1",
  "backgroundAsset": {
    "ownerModId": "AuraToolsExp",
    "assetId": "event.background.victory.standard"
  },
  "foregroundAsset": {
    "ownerModId": "AuraToolsExp",
    "assetId": "event.foreground.victory.standard"
  },
  "safeFrame": {
    "left": 0.06,
    "right": 0.06,
    "top": 0.08,
    "bottom": 0.12
  },
  "fallbackLayoutProfileId": "AuraToolsExp:team-panels.v1",
  "maximumParticipants": 8,
  "showNameplates": true
}
```

`backgroundAsset` 是稳定逻辑槽位，不意味着必须存在位图。解析器没有返回图片时，组件
渲染器使用程序主题。玩家本地配置只保存与默认值不同的字段；恢复默认即删除当前场景
覆盖，不复制默认完整对象。

### 5.2 LayoutProfile

`LayoutProfile` 是共享规划器使用的版本化、只读构图资料。玩家不直接编辑。

```json
{
  "profileId": "AuraToolsExp:team-tableau.v2",
  "family": "adaptive-tableau",
  "supportedParticipantCounts": [1, 2, 3, 4, 5, 6, 7, 8],
  "wideAssetThreshold": 0.86,
  "maximumOverlapRatio": 0.22,
  "minimumVisibleHeight": 0.30,
  "fallbackProfileId": "AuraToolsExp:team-panels.v1",
  "templates": {
    "4": ["arc-equal-a", "arc-equal-b"],
    "6": ["stagger-3x3-a", "stagger-3x3-b"],
    "8": ["stagger-4x4-a", "portrait-panels-8"]
  }
}
```

模板包含语义槽位：目标可见高度、落脚锚点、朝向、最大宽度、Z 层和标题避让区。模板不包含具体角色 ID。

### 5.3 RolePoseProfile

`RolePoseProfile` 由 owner-qualified 角色或皮肤资源提供。Alpha 边界允许写 `auto`，由资源缓存对整组帧计算联合可见边界。

```json
{
  "ownerModId": "Terrias",
  "roleId": "Terrias_wuna_wuna",
  "variantId": "default",
  "poseChannel": "victory",
  "assetId": "role.pose.victory",
  "framing": {
    "visibleBounds": "auto",
    "groundAnchorX": 0.5,
    "groundAnchorY": 0.96,
    "facing": "right",
    "bodyKind": "humanoid",
    "preferredScale": 1.0
  },
  "animation": {
    "loop": true,
    "phasePolicy": "seat-offset",
    "entranceClass": "soft"
  }
}
```

`bodyKind` 只允许通用视觉类别，例如 `humanoid`、`wide`、`floating`、`object`。它不携带 Terrias 或游戏主体语义。

## 6. 可见边界归一化

当前资源画布尺寸和透明边距差异很大，因此 v2 必须按联合 Alpha 边界映射角色：

1. 加载姿态的全部帧并计算联合 Alpha 包围盒；Alpha 阈值由共享层固定。
2. 使用联合包围盒而不是单帧包围盒，避免动画过程中尺寸和位置跳动。
3. 将联合包围盒的落脚点对齐槽位 `groundAnchor`。
4. 以目标可见高度计算缩放，再应用 provider 的有限 `preferredScale`。
5. 对 `wide`、`floating`、`object` 使用独立宽度与落脚约束。
6. 缓存键包含 owner、资产 ID、内容哈希和姿态通道；资源更新后不得复用旧边界。

自动结果越界、可见面积过小或与标题重叠时，设置页显示警告并提供有限的“水平、垂直、缩放”二级调整，不公开原始裁切坐标。

## 7. 动画与无障碍

角色层使用确定性入场顺序和动画相位：

- 入场延迟由场景、座位和事件 token 的稳定哈希计算；
- 相同队伍在所有客户端保持相同节奏；
- `Image.sprite` 只在帧序号变化时赋值；
- 五人以上时后排禁用非必要角色特效；
- 缺少专用动作时使用 Idle，但仍应用座位相位偏移；
- “减少动态效果”关闭位移、缩放脉冲、循环前景和闪烁，仅保留淡入淡出；
- 任何全屏闪烁都必须通过现有照片敏感性门禁。

程序主题组件、可选背景叠层与动态角色层由一个共享 scene renderer 管理。全屏场景仍由
一个共享 Overlay 生命周期所有，设置页嵌入模式只创建同一 renderer 的有界 host，不允许
每个事件创建自己的播放器或 Canvas。

## 8. 预览合同

设置页中的 16:9 预览窗口使用共享 `AuraCgSceneRenderer` 的嵌入模式：

```text
AuraCgScenePlanner -> AuraCgScenePlan -> AuraCgSceneAssetResolver
                   -> AuraCgSceneRenderer(hostRect)
```

全屏播放只是在同一渲染器上使用全屏 hostRect。预览不得重写构图公式或创建姓名占位版替身。

预览提供以下设计测试输入：

- 人数：1 至 8；
- 事件：七种固定场景；
- 画面：16:9、16:10、4:3、21:9；
- 资源状态：完整、缺姿态、缺皮肤、宽体、非人形；
- 动态：默认、减少动态效果。

这些输入只服务预览与验收，不写入冒险队伍数据。

## 9. 网络与协议

P0 不升级 `AuraCgSceneProtocol v1`。现有计划已经携带：

- `LayoutId`；
- `PresentationProfileId`；
- 每个成员的位置、大小、层级与镜像；
- owner-qualified 背景和角色层逻辑资产。

主机使用 v2 资料生成现有形状的处理后计划，接收端按计划展示。Alpha 边界、缓存结果、纹理和本地路径不进入 RPC。

只有后续必须把“明确焦点成员、成对互动或独立前景资产”作为权威事件事实传输时，才设计 `AuraCgSceneProtocol v2`；不得为了本地渲染便利提前扩展网络载荷。

## 10. 迁移与删除

实施完成时必须一次性完成以下切换：

1. 七个 AuraToolsExp 事件注册项已从 `team-stage.v1` 切换到 `team-tableau.v2`。
2. 单一 `DPS-CG.png` 默认背景及发布资产已删除；旧配置中的该路径只作为一次性迁移输入
   被消费为空的程序主题选择。
3. `AuraCgTeamSceneLayout` 的 v1 固定排布已删除，替换为覆盖 1 至 8 人的确定性自适应布局。
4. 设置页姓名方块假预览已删除，改为共享组件渲染器嵌入模式。
5. 删除每帧无条件重复写入 Sprite 的路径。
6. 配置若增加构图覆盖，则进行一次性 schema 迁移；不得长期保留两套 writer。

## 11. 验证矩阵

### 纯行为

- 1 至 8 人每种人数都得到稳定布局；
- 同输入重复规划得到字节等价的处理后计划；
- 可见面积差、遮挡率、边界裁切和标题遮挡均在预算内；
- 宽体与非人形资源触发面板回退；
- 缺姿态、缺皮肤和缺资源按固定链路降级；
- v1 网络计划仍不包含路径、图片、Alpha 边界或本地缓存键。

### Unity 视觉

- `1280x720` 与 `922x838`；
- 16:9、16:10、4:3、21:9；
- 开场、普通胜利、三种特殊胜利、失败、结算；
- 1、2、3、4、6、8 人代表样本；
- 默认与减少动态效果；
- 非空像素、边界、安全区、遮挡和文本对比检查。

### 性能

- 记录每场 Sprite 实际赋值次数，不得按渲染帧无条件增长；
- 记录背景 Canvas 和角色 Canvas rebuild；
- 八人、每人二十四帧资源下验证峰值内存与释放；
- 场景结束、战斗重开、模块关闭和退出冒险后无残留 Sprite、Material 或 Canvas。

## 12. 实施顺序

1. `RolePoseProfile`、联合 Alpha 边界缓存与姿态回退。
2. `LayoutProfile`、人数模板和稳定评分器。
3. `SceneProfile`、七套程序主题令牌和可选背景叠层。
4. 统一 `AuraCgSceneRenderer`，切换全屏与嵌入预览。
5. 动画相位、减少动态效果、Sprite 赋值与 Canvas 性能修复。
6. 配置迁移、v1 删除、注册表切换、测试与发布物同步。

每一步都必须成为最终架构的一部分；不保留临时第二播放器、临时网络字段或长期双布局路径。

## 13. 调研依据

- [FINAL FANTASY XIV Patch 6.3 portraits](https://na.finalfantasyxiv.com/lodestone/topics/detail/2ebebcdeedfecd2af0bf4cd5ce2d707e35f50d70)
- [Splatoon 3 victory emotes](https://splatoon.nintendo.com/en/weapons/)
- [Splatoon 3 update history](https://en-americas-support.nintendo.com/app/answers/detail/a_id/61257/)
- [Knockout City victory and defeat poses](https://blog.playstation.com/?p=346625)
- [Overwatch team-focused end flow](https://gameinformer.com/b/features/archive/2016/08/24/designing-overwatch-from-titan-to-torbjorn.aspx)
- [Destiny 2 commendation accessibility](https://www.bungie.net/7/en/News/article/destiny-2-accessibility-set)
- [Unity UI optimization](https://unity.com/how-to/unity-ui-optimization-tips)
- [Xbox visual motion guideline](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/117)
- [Adobe automatic image cropping research](https://research.adobe.com/publication/automatic-image-cropping-using-visual-composition-boundary-simplicity-and-content-preservation-models/)
- [Microsoft AutoCollage research](https://www.microsoft.com/en-us/research/publication/autocollage/)
