---
name: sunexp-mod-dev
description: Project-local skill for producing and debugging SunExp mod content for Witch's Apocalyptic Journey. Use when editing or reviewing SunExp C# DLL scripts, current Managed API compatibility, runtime hooks, multiplayer behavior, CSV data and localization, cards, buffs, relics, card packs, roles, dialogue, events, map nodes, assets, or validation rules in this repository.
---

# SunExp Mod Dev

Use this skill only inside this repository. Treat `SunExp/` as the shipped mod
surface and `SunExp-Dev/` as the default implementation surface for behavior.
Production behavior lives in C# DLL entry points called from CSV. The `luaEnv`
bridge in `SunExp-Dev/Entry.cs` is a host interop detail, not a production Lua
implementation path.

## Workflow

1. Inspect the current feature surface before editing:
   - `SunExp-Dev/**/*.cs`
   - `SunExp/Data/**/*.csv`
   - `SunExp/Text/**/*.csv`
   - `SunExp/ModConfig.json`
   - `SunExp/audio.registry.json` when audio, vocal, or BGM behavior changes.
   - release-facing docs only when behavior, counts, or user-facing claims change.
2. Load only the relevant reference:
   - Card, Buff, Relic, CardPack fields: `references/csv-schema.md`
   - C# boundaries, Managed signature drift, hook containment, multiplayer authority, and decompiled-reference routes: `references/csharp-authoring-boundaries.md`
   - Role, dialogue, and event expansion: `references/expansion-role-dialogue-event.md`
   - Map-event authoring checklist: `references/solar-event-expansion.md`
   - Validation expectations: `references/validation-rules.md`
   - For EventList, Text/EventList, map-visible event, and event helper work, also use the project-local `sunexp-event-dev` skill.
3. Keep behavior in C# by default:
   - CSV script columns should call `CS.SunExp.Dll.Scripting.*` entry points.
   - Put card, buff, relic, role, boss, and event behavior in the matching `SunExp-Dev/Scripting/*Scripts.cs` file.
   - Put shared game-facing wrappers in `SunExp-Dev/GameApi/`, reusable implementation code in `SunExp-Dev/Mechanics/`, and IDs/utilities in `SunExp-Dev/Infrastructure/`.
   - Put runtime hook and UI integration code in `SunExp-Dev/Hooks/`.
4. Keep Data and Text rows synchronized. Any new card, buff, relic, card pack, role, dialogue, map, or event needs both config and localized text when the template has both sides.
5. Prefer existing SunExp C# helpers over inline CSV logic. Add a shared helper only when multiple scripts need the same behavior or nil-safe wrapper.
6. Treat the repository `Managed/` assemblies as the current compile contract. Use the decompiled reference to understand behavior, then verify signatures against current assemblies when APIs may have changed.
7. Check authoring boundaries before validation when edits touch new C# entry points, hooks, CSV script columns, resource paths, or localized descriptions.
8. Run validation before finishing:

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-event-dev\scripts\validate-sunexp-events.ps1 # when events or maps change
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

## Hard Rules

- Do not add script implementation paths outside the C# DLL production surface.
- Do not paste official template snippets directly into CSV script columns. CSV script columns should call stable SunExp C# entry points.
- Use full mod IDs when referencing SunExp-defined content.
- Keep player-facing text, dynamic descriptions, release notes, and behavior in sync when rows or scripts change.
- Do not write battle-only or run-only state back into base CSV `Data/*` rows.
- Do not bind directly to a Managed method whose signature has drifted across supported game versions. Put reflection-based current/legacy dispatch and a deterministic fallback in `GameApi/`.
- Do not let one independent fight-start or lifecycle action abort later actions. Isolate fragile steps and log each failure with its step name.
- Only server authority may advance shared multiplayer progression; clients may update local presentation and player-scoped state.
- Leave `Text/Relic.Tag` blank unless a visible relic label is intentionally needed; it is separate from `Data/Relic.PackBelong`.

## Authoring Checks

- CSV script columns should stay short and delegate to `CS.SunExp.Dll.Scripting.*`.
- New public CSV-callable C# methods should have stable names and small parameter lists.
- Hook code should be isolated under `SunExp-Dev/Hooks/` and verified against the decompiled method signature before use.
- New `MapTree.Node` instances must receive a valid deterministic `NodeDice` before entering map lists or sync arrays.
- Rebuild `SunExp/Scripts/Entry.dll` after every C# compatibility or hook change; source edits alone do not change shipped behavior.
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
