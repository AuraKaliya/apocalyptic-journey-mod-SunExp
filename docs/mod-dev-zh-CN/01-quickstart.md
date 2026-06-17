# 快速开始

这是在本工作区新增一项 MOD 内容的最短安全路径。

## 选择编写路径

适合使用官方 Lua 路径的情况：

- 行为简单，可以留在 `Scripts/Entry.lua`
- 只需要改表、资源重定向或很小的 Hook
- 正在跟随 `apocalyptic-journey-mod-tutorial/ModTemplate`

适合使用 C# DLL 路径的情况：

- 行为分支较多，或需要共享 helper
- 需要稳定访问游戏对象类型
- Hook 用 C# 更容易维护
- 正在开发 SunExp 风格项目

本工作区的 SunExp 风格项目通常采用：

- 发布内容放在 `<ModName>/`
- C# 源码放在 `<ModName>-Dev/`
- 编译产物复制到 `<ModName>/Scripts/Entry.dll`

## 新增内容

1. 在 `Data/<Table>/<file>.csv` 添加或编辑数据行。
2. 如果该表有文本侧，在 `Text/<Table>/<file>.csv` 添加或编辑匹配行。
3. 把引用资源放进 `ModResource/`，或使用已知原版资源路径。
4. 脚本列保持短调用。
5. DLL 项目中，把行为放进 C# `Scripting/` 方法。
6. 新增 helper 前先复用 `GameApi/`、`Mechanics/`、`Infrastructure/`。
7. 运行对应验证命令。

## ID 规则

运行时 MOD ID 由以下部分组成：

```text
ModName_FileName_Id
```

例如 `SunExp/Data/Card/sunexp.csv` 中 `Id = spark` 的卡牌，运行时完整 ID
通常是：

```text
SunExp_sunexp_spark
```

脚本引用 MOD 自定义内容时，优先使用完整运行时 ID。以 `*` 开头的 ID 通常
不会进入随机池，适合职业卡、衍生物、固定事件牌等内容。

## 脚本列

SunExp 风格 C# 项目的 CSV 脚本列应像桥接调用：

```csv
CS.SunExp.Dll.Scripting.CardScripts.Use(self, "spark");
```

避免在 CSV 单元格里写长逻辑。长脚本难测试、难 diff，也容易和显示文本不同步。

## 验证

SunExp 默认验证路径：

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

本地验证不能证明 Unity 运行时语义。UI Hook、地图流程和深层场景交互仍需要
进游戏确认。
