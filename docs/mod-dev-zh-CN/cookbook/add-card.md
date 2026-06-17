# 添加一张卡牌

这份清单面向 SunExp 风格的 C# DLL 项目。其他 `*-Dev` 项目可替换命名使用。

## 1. 添加 Data

在以下位置添加行：

```text
<ModName>/Data/Card/<file>.csv
```

常见必需字段：

- `Id`
- `Rarity`
- `Expend`
- `Tag`
- `InitScript`
- `DrawScript`
- `UseScript`
- `DropScript`
- `Icon`
- `Effects`
- `Action`
- `PackBelong`

使用短脚本桥：

```csv
CS.SunExp.Dll.Scripting.CardScripts.Init(self, "new_card");
CS.SunExp.Dll.Scripting.CardScripts.Use(self, "new_card");
```

## 2. 添加 Text

在以下位置添加匹配行：

```text
<ModName>/Text/Card/<file>.csv
```

保持 `{0}` 等占位符与 `InitScript` 中的动态描述一致。

## 3. 添加行为

在以下文件添加 case 或 helper：

```text
<ModName>-Dev/Scripting/CardScripts.cs
```

如果多张卡需要同一操作，添加或复用 helper：

```text
<ModName>-Dev/GameApi/
<ModName>-Dev/Mechanics/
```

## 4. 添加资源

把卡图放进 `ModResource/`，或使用已知原版资源路径。

MOD 资源路径通常以以下前缀开头：

```text
Mods/<ModName>/ModResource/...
```

## 5. 验证

SunExp 默认：

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

手动检查：

- 目标选择与 `AttackCardItem` / `CommonCardItem` 匹配
- 费用、伤害、护盾和描述一致
- `PackBelong` 指向存在的 CardPack
- 以 `*` 开头的衍生牌或固定牌确实应排除随机池
