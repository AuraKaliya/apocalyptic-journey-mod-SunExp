# AuraToolsExp 独立 Unity UI Preview Player

> 妙妙工具内部组件的下一轮视觉重构见
> [toolbox-ui-component-redesign-plan.md](toolbox-ui-component-redesign-plan.md)。

## 1. 目标

`AuraToolsUnityUiPreview/` 是一个独立 Unity 2022.3.62f3c1 项目。它不启动
游戏、不加载 `Witch.dll`、不读取存档、不连接网络，也不进入任何玩法场景。

Player 复刻完整设置窗口及五个顶层标签：

1. 音画；
2. 游戏；
3. 反馈；
4. 键位；
5. 妙妙工具。

原生设置项依据当前游戏构建 `1.0.24605918` 的 `SettingTable` 默认键和
`SettingUI` 生命周期整理。妙妙工具页使用与生产实现相同的模块 ID、分类、
颜色、144/52/84 布局指标和 30 个图标/控件资源。

## 1.1 原生视觉基准

首版 Player 只复刻了信息结构，视觉上错误地采用了现代灰黑扁平面板。根据
实际游戏截图完成第二轮校准后，Player 现在使用：

- 游戏原生按钮九宫格；
- 游戏原生大/小面板九宫格；
- 游戏原生横向选择器底图；
- `#070328` 舞台底色与靛蓝设置页；
- 大号暖白标题和正文；
- “开启 / 关闭”成组复选框与蓝紫色勾选；
- 金色端帽选择器、原生按钮式返回入口；
- 妙妙工具外壳保留原生金框，内部使用独立的轻量组件资源。

妙妙工具内部现已进入 V2：原生大金框只保留在设置窗口外壳；分类、页头、
搜索、设置按钮和启用复选框使用新的低装饰密度 Toolbox V2 资源。默认图标
按钮不显示金框，只有 Hover/Focus 才提升金色边；模块行提高到 96，分类栏
提高到 168。

构建脚本会从 AuraToolsExp/Terrias 已有原生资源同步五张基准图片，并强制
Unity 以 NPOT 不缩放、无压缩、Point、Clamp 方式导入，避免运行时裁剪漂移。

## 2. 运行

构建 Player 并运行自动截图：

```powershell
tools\Build-AuraToolsUnityUiPreview.ps1
```

构建、验证并打开交互 Player：

```powershell
tools\Build-AuraToolsUnityUiPreview.ps1 -Open
```

直接打开已经构建的 Player：

```powershell
tools\Build-AuraToolsUnityUiPreview.ps1 -SkipBuild -SkipCapture -Open
```

输出目录：

```text
output/unity/aura-tools-ui-preview/
```

## 3. 原生设置页

### 音画

- 分辨率、窗口模式、画面质量和帧率；
- 全局、音乐、效果和旁白音量；
- 语言、字体、角色配音和低配模式。

### 游戏

- 推演剧情、加速模式、伤害数字和卡牌自指；
- 角色配音、动画速度和选牌确认；
- 悬停提示、战前卡组和自动保存。

### 反馈

- 多行反馈输入；
- 发送按钮和本地状态；
- 明确禁止网络上传。

### 键位

- 重开战斗、结束回合和结束选牌；
- 设置、卡组和目标切换；
- 重置按键入口。

## 4. 妙妙工具

- 左侧分类栏和分类数量；
- 跨分类搜索、空结果和扩展模块；
- 84 像素模块行、图标、摘要、说明、设置按钮和 Switch；
- 默认、长文本、异常、空结果和扩展五种场景；
- 二级设置 Overlay；
- 完全不透明工作区和隐藏的洋红色原生内容探针。

## 5. Player 自动验证

Player 自己切页、切场景、渲染并输出十二张截图：

- 四张原生设置页；
- 五种妙妙工具场景；
- 920 x 848 和 1024 x 640 响应式截图；
- 妙妙工具二级 Overlay。

`captures/report.json` 验证：

- 当前标签与页面可见性一致；
- 妙妙工具打开时四张原生页全部停用；
- 工作区 alpha 为 1；
- 中心射线命中妙妙工具拥有的 UI；
- 分类文字不截断；
- 模块行保持 84 像素；
- 截图非空且十二张截图哈希互不相同；
- 洋红色底层探针像素为零；
- 金色窗口外框不触碰截图边缘。
- 顶层标签使用原生按钮九宫格；
- 设置窗口使用原生大面板九宫格；
- 原生页包含成组开启/关闭复选框；
- 妙妙工具使用原生方形复选框而不是通用 Pill Switch。

## 6. 与 HTML 预览的分工

HTML + Playwright 适合快速编辑、响应式布局和交互回归。Unity Player 负责
验证真实 UGUI 的 CanvasScaler、RectTransform、LayoutGroup、ScrollRect、
ColorBlock、EventSystem、GraphicRaycaster 和相机渲染。

两者都不替代最终游戏内检查。独立 Player 使用代码复刻的原生设置页和
Windows 动态中文字体，不包含游戏原始 Prefab、ButtonManager 动画、音效、
本地化包或其它受游戏运行时所有的资源。
