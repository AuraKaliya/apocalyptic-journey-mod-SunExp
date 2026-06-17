# Add a Card

This checklist targets SunExp-style C# DLL projects. Adapt names for other
`*-Dev` projects.

## 1. Add Data

Add a row under:

```text
<ModName>/Data/Card/<file>.csv
```

Required fields usually include:

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

Use a short script bridge:

```csv
CS.SunExp.Dll.Scripting.CardScripts.Init(self, "new_card");
CS.SunExp.Dll.Scripting.CardScripts.Use(self, "new_card");
```

## 2. Add Text

Add a matching row under:

```text
<ModName>/Text/Card/<file>.csv
```

Keep placeholders such as `{0}` aligned with `InitScript` dynamic descriptions.

## 3. Add Behavior

Add cases or helpers in:

```text
<ModName>-Dev/Scripting/CardScripts.cs
```

If several cards need the same operation, add or reuse a helper under:

```text
<ModName>-Dev/GameApi/
<ModName>-Dev/Mechanics/
```

## 4. Add Assets

Put card images under `ModResource/` or use a known original-game path.

For MOD resources, CSV paths normally start with:

```text
Mods/<ModName>/ModResource/...
```

## 5. Validate

For SunExp:

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

Manual checks:

- target selection matches `AttackCardItem` vs `CommonCardItem`
- cost, damage, block, and descriptions match
- `PackBelong` points to an existing CardPack
- any generated or token card with `*` is intentionally excluded from pools
