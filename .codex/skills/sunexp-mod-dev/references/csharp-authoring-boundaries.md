# C# Authoring Boundaries

Use this reference when deciding where new SunExp production code belongs or
when checking game API shape in the decompiled reference project.

## SunExp Code Boundaries

- `SunExp/Dev/Scripting/*Scripts.cs`: public static methods called directly by CSV script columns.
- `SunExp/Dev/GameApi/*`: wrappers around game objects, `ScriptExecutor`, player APIs, buffs, cards, vars, and safe runtime access.
- `SunExp/Dev/Infrastructure/*`: constants, logging, dictionary helpers, parsing helpers, and other low-level support.
- `SunExp/Dev/Mechanics/*`: reusable implementation code shared by multiple scripting entry points.
- `SunExp/Dev/Hooks/*`: code that attaches to game methods, event listeners, or lifecycle points.
- `SunExp/Data/**/sunexp.csv`: configuration rows and short `CS.SunExp.Dll.Scripting.*` calls.
- `SunExp/Text/**/sunexp.csv`: localized player-facing text that must match the Data rows.

Do not put long implementation logic in CSV script columns. Add or reuse a C#
entry point, then call that entry point from CSV.

## Host Bridge

`SunExp/Dev/Entry.cs` may use the game's XLua host objects, such as
`ScriptExecutor.luaEnv`, to expose the SunExp C# assembly to CSV script calls.
This bridge is necessary interop. It must not grow into production `.lua` files
or old dynamic helper registration.

## Decompiled Reference Routes

Use the decompiled project only to verify production boundaries, method names,
signatures, and comparable official script shape. Do not copy large chunks of
decompiled code into the mod.

Primary locations:

- `开发参考资料/反编译文件夹/AllScripts/AllScripts.cs`: official compiled script examples and `ScriptExecutor` usage.
- `开发参考资料/反编译文件夹/Witch`: game-side classes and managers.
- `开发参考资料/反编译文件夹/Witch.Core`: core data types and shared runtime structures.
- `开发参考资料/反编译文件夹/Assembly-CSharp`: Unity-side managers, UI, and scene behavior when relevant.

Useful searches:

```powershell
rg -n "ScriptExecutor|AddBuff|RunImmediately|AddDescription" "开发参考资料\反编译文件夹\AllScripts" "开发参考资料\反编译文件夹\Witch"
rg -n "class CommonCardItem|class AttackCardItem|TrueUse|RunScript" "开发参考资料\反编译文件夹"
rg -n "EventList|Choice1|Choice2|EndEvent|ContinueEvent" "开发参考资料\反编译文件夹\AllScripts" "开发参考资料\反编译文件夹\Witch"
rg -n "MapSelectUI|NormalMapManager|MapManager|SelectNode" "开发参考资料\反编译文件夹"
rg -n "AddEventListener|RemoveEventListener|EventCenter|EventDispose" "开发参考资料\反编译文件夹"
```

## Placement Rules

- Add a new `Scripting` method when CSV needs a new callable operation.
- Add a `GameApi` wrapper when multiple scripts need the same game-object access or null-safe call.
- Add `Infrastructure` constants before repeating string IDs or variable keys.
- Add `Hooks` code only after verifying the target method or event name in the decompiled reference.
- Keep decompiled-reference findings out of `SKILL.md`; use them only to guide the current edit.

## Validation

After C# changes, run:

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```
