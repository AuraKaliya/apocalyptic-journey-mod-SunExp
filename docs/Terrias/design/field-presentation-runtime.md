# 场地视觉表现

本轮为灼热天幕、轮回花庭和月之领域加入专属远景、场景内环境效果，以及手牌/操作区背光。表现由本地当前场地快照驱动，保持原有“同类叠层、异类替换”的机制和网络权威关系。

## 玩家可见行为

| 场地 | 远景 | 持续效果 | 触发反馈 |
| --- | --- | --- | --- |
| 灼热天幕 | 暗红天幕与远处火山废墟 | 上升的像素余烬、暖色地面光、手牌区暖光 | 入场、叠层及回合开始时加强一次地面亮度 |
| 轮回花庭 | 青绿花庭与古老环形遗迹 | 缓慢飘落的花瓣、地面花纹与光环、柔和背光 | 入场、叠层及回合开始时花纹和光环脉冲 |
| 月之领域 | 月海与悬浮遗迹 | 银蓝色微光、缓慢扩散的地面涟漪、手牌背后的月光 | 入场与本地月反应结算后的脉冲 |

切换场地时各主题直接交叉淡化。同一场地增加层数只改变强度并给予一次短反馈，不重播背景入场。普通移除时淡出；进入胜利、失败或逃跑结算、重开及界面销毁时清理。

背景仅覆盖原生 `background` 排序层，保留中景平台、前景树木及原生 `SceneInfo`、地面标记、相机边界、地区名称和 BGM。资源缺失时仍可显示场地纹样和氛围，并保留原生背景。

## 配置

配置位于 [visual.registry.json](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias/visual.registry.json)。文件按现有视觉注册表生命周期载入，修改后重新载入 MOD/重启游戏生效。

```json
"fieldPresentation": {
  "enabled": true,
  "quality": "standard",
  "intensity": 0.8,
  "reducedMotion": false,
  "backgroundsEnabled": true
}
```

- `enabled`：整套场地视觉开关；原有场地 HUD 继续显示机制信息。
- `quality`：`standard` 或 `low`。标准模式最多每秒重建 30 次几何、每个主题最多 36 个粒子；低画质最多每秒 15 次、12 个粒子。
- `intensity`：0～1 的环境与 UI 光影强度。0 停止整套效果；背景的混合强度另由每个场地的 `backgroundOpacity` 控制。
- `reducedMotion`：关闭脉冲和持续漂移，保留静态场地提示；几何更新预算为每秒 12 次。
- `backgroundsEnabled`：是否显示专属远景，可单独保留环境与 UI 光影。

`fields` 中每个条目通过 `id` 对应场地 slug，包含背景路径、主色、点缀色、粒子数量和背景不透明度。背景适合使用不含人物/UI/近景平台的 16:9 不透明图；渲染时按当前视口等比例覆盖并居中裁切。

另在共享 feature switch 中注册 `Terrias / Field.EnvironmentPresentation`，默认开启。现有本地 GameVar `TerriasFieldVisuals` 可以用 `false` / `off` 显式关闭；原生缺省值 `0` 按既有约定表示未设置。

## 运行时边界

- `FieldPresentationRuntime` 订阅现有战斗路由和 `FieldApi.Changed`。延迟刷新按战斗代次隔离，结算后的旧任务不能重新创建效果。
- `FieldPresentationState` 只处理本地视觉插值、叠层强度和反馈脉冲，不写入战斗状态。
- `FieldPresentationSceneApi` 定位当前 FightUI、背景、相机、地面和手牌/操作区域，使用现有战斗状态门禁。
- `FieldPresentationView` / `FieldVisualMesh` 只创建并管理自己的材质、网格和对象，不改写原生卡面、背景 Renderer、灯光或后处理材质。
- 新背景位于 `background`，地面效果位于 `middleground`，飘落粒子位于 `foreground`；UI 背光使用低于原生手牌组的 `Default` 排序。
- 背景四边形在镜头更新后的 LateUpdate 每帧贴合视口；低画质只降低环境纹样和粒子的更新频率，避免镜头运动时露出原背景边缘。
- 所有效果对象都没有交互 Collider/GraphicRaycaster；UI 效果组不阻挡射线。
- PNG 必须通过原生支持的 `Load<Texture>` 路径读取，再转换为 `Texture2D`。预热和实际显示使用同一类型及缓存类别。
- 背景通过既有资源预热队列在冒险阶段择机加载；不在战斗中继续执行无关预热。
- `FieldPresentationSignals` 的监听异常与机制结算隔离。月反应只在已完成的本地反应流程中发布反馈，不增加 RPC 或重复执行反应。

## 美术资源

三个首版背景通过内置 imagegen 生成，原图按工具输出复制到 MOD，未进行额外位图编辑：

- [月之领域](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias/ModResource/Images/Field/moon_domain.png)
- [灼热天幕](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias/ModResource/Images/Field/scorching_canopy.png)
- [轮回花庭](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias/ModResource/Images/Field/samsara_garden.png)
- [完整提示词](D:/Project/Apocalypic-journey-mods-creator/ModExp/docs/Terrias/design/field-background-prompts.json)

## 验证入口

`tools/Test-TerriasFieldPresentation.ps1` 检查视觉插值、快速替换、快照重复、层数上限、低动态、清理、异常隔离、配置和背景资源。

`tools/Test-TerriasFieldPresentationUnity.ps1 -UnityPath <Unity 6000.0.46f1 的 Unity.exe> -GameDataDirectory <游戏 Data 目录>` 使用当前生产渲染源码，在 Unity/URP 中检查实际像素变化、原生卡面保护、射线交互、缺图退化、开关、重复战斗及结算取消。它提取安装包中的 Forest 图层用于验证场景，输出截图及带源文件 SHA-256 的结果。此场景中的控制器边界使用测试替身；截图不冒充完整实机战斗。

渲染证据在 `output/field-presentation-unity`，成功运行后生成 `latest.json`。正式产品构建/发布仍使用 `tools/Build-TerriasDll.ps1` 进入统一主产品事务；复制到游戏安装目录使用 `tools/Deploy-AuraProducts.ps1`。
