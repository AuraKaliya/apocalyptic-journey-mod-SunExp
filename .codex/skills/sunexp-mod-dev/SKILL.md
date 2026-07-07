---
name: sunexp-mod-dev
description: Project-local routing and general-development skill for SunExp mod work in Witch's Apocalyptic Journey. Use when editing or reviewing SunExp shipped mod content, C# DLL scripts, CSV data and localization, cards, buffs, relics, card packs, roles, dialogue, assets, validation, shared initialization registration, tool configuration overrides, multiplayer sync, multi-mod sync, timing and duplicate suppression, or when deciding which specialized SunExp skill to load for architecture, Solar Memory, events, shared runtime, visual runtime, card art, or skill evolution.
---

# SunExp Mod Dev

Use this skill only inside this repository. Treat `SunExp/` as the shipped mod
surface and `SunExp-Dev/` as the default implementation surface for behavior.
Production behavior lives in C# DLL entry points called from CSV. The `luaEnv`
bridge in `SunExp-Dev/Entry.cs` is a host interop detail, not a production Lua
implementation path.

## Specialist Routing

Use the smallest specialist set that covers the task:

- `sunexp-architecture-dev`: C# layer boundaries, `GameApi` split, handler
  registries, Managed compatibility, hook containment, or architecture tests.
- `sunexp-solar-memory-dev`: Solar Memory mode, journey, map node pools,
  preparation flow, finale, fixed bosses, multiplayer role commit, and sync
  repair.
- `sunexp-event-dev`: non-Solar-Memory EventList/Map rows, story chains,
  reward helpers, and ordinary map-visible events.
- `sunexp-shared-runtime-dev`: Aura shared runtimes, shared resources, Journey,
  Skin, Audio, BGM, StarterDeck, CG, UI safety, initialization registration,
  tool-local configuration overrides, cross-mod sync models, RPC authority,
  shared DLL packaging, or shared release gates.
- `sunexp-visual-runtime-dev`: `visual.registry.json`, VisualBundles, shaders,
  card visual skins/effects, Skill CG, animated icons, map-node visuals, Star
  Score HUD, Wuna orbit fire, or visual runtime validation.
- `sunexp-card-art-style`: card-face art, relic icons, contact sheets, and
  bitmap image asset validation.
- `sunexp-skill-evolution`: updating the project-local skills from development
  traces or planned renaming/generalization.

## Workflow

1. Inspect the current feature surface before editing:
   - `SunExp-Dev/**/*.cs`
   - `SunExp/Data/**/*.csv`
   - `SunExp/Text/**/*.csv`
   - `SunExp/ModConfig.json`
   - `SunExp/audio.registry.json` when audio, vocal, or BGM behavior changes.
   - `SunExp/visual.registry.json` and `SunExp/SharedResources/*` when runtime
     visuals, Skill CG, or shared resource manifests change.
   - release-facing docs only when behavior, counts, or user-facing claims change.
2. Load only the relevant reference:
   - Card, Buff, Relic, CardPack fields: `references/csv-schema.md`
   - C# boundaries, Managed signature drift, hook containment, multiplayer authority, player-scoped rewards, presentation events, and decompiled-reference routes: `references/csharp-authoring-boundaries.md`
   - Role, dialogue, and event expansion: `references/expansion-role-dialogue-event.md`
   - Map-event authoring checklist: `references/solar-event-expansion.md`
   - Validation expectations: `references/validation-rules.md`
   - For C# architecture refactors, also use `sunexp-architecture-dev`.
   - For Solar Memory work, also use `sunexp-solar-memory-dev`.
   - For shared runtime work, initialization registration, AuraToolsExp local
     config overrides, cross-mod sync, payload guard, timing, or duplicate
     suppression, also use `sunexp-shared-runtime-dev`.
   - For runtime visual work, also use `sunexp-visual-runtime-dev`.
   - For EventList, Text/EventList, map-visible event, and event helper work, also use the project-local `sunexp-event-dev` skill.
3. Keep behavior in C# by default:
   - CSV script columns should call `CS.SunExp.Dll.Scripting.*` entry points.
   - Put card, buff, relic, role, boss, and event behavior in the matching `SunExp-Dev/Scripting/*Scripts.cs` file.
   - Put shared game-facing wrappers in `SunExp-Dev/GameApi/`, reusable implementation code in `SunExp-Dev/Mechanics/`, and IDs/utilities in `SunExp-Dev/Infrastructure/`.
   - Put runtime hook and UI integration code in `SunExp-Dev/Hooks/`.
   - Put feature runtimes that are not CSV entry points under `SunExp-Dev/Features/`.
4. Keep Data and Text rows synchronized. Any new card, buff, relic, card pack, role, dialogue, map, or event needs both config and localized text when the template has both sides.
5. Prefer existing SunExp C# helpers over inline CSV logic. Add a shared helper only when multiple scripts need the same behavior or nil-safe wrapper.
6. Treat the repository `Managed/` assemblies as the current compile contract. Use the decompiled reference to understand behavior, then verify signatures against current assemblies when APIs may have changed.
7. Check authoring boundaries before validation when edits touch new C# entry points, hooks, CSV script columns, resource paths, or localized descriptions.
8. Run validation before finishing. Build and test commands that write
   `SunExp.Aura.dll` must be serial:

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpArchitecture.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-event-dev\scripts\validate-sunexp-events.ps1 # when events or maps change
tools\Build-SunExpVisualBundle.ps1 # when VisualAssets or VisualBundles change
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
- Do not model player-independent rewards as shared host rewards. If each
  player should choose and receive a reward independently, use player-scoped
  UI/application and record only that player's result.
- Treat synchronized visual effects, projections, skins, and temporary UI as
  presentation events with explicit duplicate suppression and lifecycle cleanup;
  do not let remote observation hooks become event originators.
- Leave `Text/Relic.Tag` blank unless a visible relic label is intentionally needed; it is separate from `Data/Relic.PackBelong`.
- Do not run `tools\Build-SunExpDll.ps1` and `tools\Test-SunExpCSharp.ps1` in parallel; both can write the same DLL output.

## Authoring Checks

- CSV script columns should stay short and delegate to `CS.SunExp.Dll.Scripting.*`.
- New public CSV-callable C# methods should have stable names and small parameter lists.
- Hook code should be isolated under `SunExp-Dev/Hooks/` and verified against the decompiled method signature before use.
- Runtime visual declarations should be centralized in `SunExp/visual.registry.json`
  and the visual runtime skill, not hidden in feature-specific hard-coded paths.
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

Run architecture assertions:

```powershell
tools\Test-SunExpArchitecture.ps1
```

Run SunExp CSV/resource validation:

```powershell
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```
