---
name: sunexp-mod-dev
description: Project-local skill for producing SunExp mod content for Witch's Apocalyptic Journey. Use when editing or reviewing SunExp C# DLL scripts, CSV data and localization, card/buff/relic/card-pack rows, role data, dialogue, events, assets referenced by CSV rows, validation rules, or C# authoring boundaries in this repository.
---

# SunExp Mod Dev

Use this skill only inside this repository. Treat `SunExp/` as the shipped mod
surface and `SunExp/Dev/` as the default implementation surface for behavior.
Production behavior lives in C# DLL entry points called from CSV. The `luaEnv`
bridge in `SunExp/Dev/Entry.cs` is a host interop detail, not a production Lua
implementation path.

## Workflow

1. Inspect the current feature surface before editing:
   - `SunExp/Dev/**/*.cs`
   - `SunExp/Data/**/sunexp.csv`
   - `SunExp/Text/**/sunexp.csv`
   - `SunExp/ModConfig.json`
   - release-facing docs only when behavior, counts, or user-facing claims change.
2. Load only the relevant reference:
   - Card, Buff, Relic, CardPack fields: `references/csv-schema.md`
   - C# implementation boundaries and decompiled-reference search routes: `references/csharp-authoring-boundaries.md`
   - Role, dialogue, and event expansion: `references/expansion-role-dialogue-event.md`
   - Map-event authoring checklist: `references/solar-event-expansion.md`
   - Validation expectations: `references/validation-rules.md`
   - For EventList, Text/EventList, map-visible event, and event helper work, also use the project-local `sunexp-event-dev` skill.
3. Keep behavior in C# by default:
   - CSV script columns should call `CS.SunExp.Dll.Scripting.*` entry points.
   - Put card, buff, relic, role, and event behavior in the matching `SunExp/Dev/Scripting/*Scripts.cs` file.
   - Put shared game-facing wrappers in `SunExp/Dev/GameApi/`, reusable implementation code in `SunExp/Dev/Mechanics/`, and IDs/utilities in `SunExp/Dev/Infrastructure/`.
4. Keep Data and Text rows synchronized. Any new card, buff, relic, card pack, role, dialogue, map, or event needs both config and localized text when the template has both sides.
5. Prefer existing SunExp C# helpers over inline CSV logic. Add a shared helper only when multiple scripts need the same behavior or nil-safe wrapper.
6. Check authoring boundaries before validation when edits touch new C# entry points, hooks, CSV script columns, resource paths, or localized descriptions.
7. Run validation before finishing:

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

## Hard Rules

- Do not add script implementation paths outside the C# DLL production surface.
- Do not paste official template snippets directly into CSV script columns. CSV script columns should call stable SunExp C# entry points.
- Use full mod IDs when referencing SunExp-defined content.
- Keep player-facing text, dynamic descriptions, release notes, and behavior in sync when rows or scripts change.
- Do not write battle-only or run-only state back into base CSV `Data/*` rows.
- Leave `Text/Relic.Tag` blank unless a visible relic label is intentionally needed; it is separate from `Data/Relic.PackBelong`.

## Authoring Checks

- CSV script columns should stay short and delegate to `CS.SunExp.Dll.Scripting.*`.
- New public CSV-callable C# methods should have stable names and small parameter lists.
- Hook code should be isolated under `SunExp/Dev/Hooks/` and verified against the decompiled method signature before use.
- Data/Text CSV rows, icon paths, and localized descriptions should be updated in the same change.
- Old dynamic helper calls are not allowed in CSV script columns; use C# entry points.

## Useful Commands

Inventory current content:

```powershell
.codex\skills\sunexp-mod-dev\scripts\extract-sunexp-inventory.ps1
```

Build the C# DLL:

```powershell
tools\Build-SunExpDll.ps1
```

Run C# tests:

```powershell
tools\Test-SunExpCSharp.ps1
```

Run SunExp CSV/resource validation:

```powershell
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```
