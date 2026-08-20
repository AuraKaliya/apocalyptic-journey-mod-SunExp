---
name: terrias-mod-dev
description: Project-local routing and general-development skill for Terrias mod work in Witch's Apocalyptic Journey. Use when editing or reviewing Terrias shipped mod content, C# DLL scripts, CSV data and localization, cards, buffs, relics, card packs, roles, dialogue, assets, validation, shared initialization registration, tool configuration overrides, multiplayer sync, multi-mod sync, timing and duplicate suppression, or when deciding which specialized Terrias skill to load for complete-solution gating, architecture, Solar Memory, events, shared runtime, visual runtime, card art, or skill evolution.
---

# Terrias Mod Dev

Use this skill only inside this repository. Treat `Terrias/` as the shipped mod
surface and `Terrias-Dev/` as the default implementation surface for behavior.
Production behavior lives in C# DLL entry points called from CSV. The `luaEnv`
bridge in `Terrias-Dev/Entry.cs` is a host interop detail, not a production Lua
implementation path.

Terrias is the content mod in the Aura ecosystem. AuraToolsExp is the tool mod.
Both are sibling consumers of Aura shared/core layers. Terrias registers
Terrias-owned content into shared data; AuraToolsExp configures and manages
shared feature modules through shared protocols. Do not make either mod depend
on the other.

## Specialist Routing

Use the smallest specialist set that covers the task:

- `terrias-complete-solution-gate`: load first for every defect solution,
  refactor, migration, compatibility repair, legacy-path replacement, or
  technical-debt cleanup. It rejects temporary stopgaps and makes migration plus
  removal of the retired implementation part of completion.
- `terrias-architecture-dev`: C# layer boundaries, `GameApi` split, handler
  registries, Managed compatibility, hook containment, or architecture tests.
- `terrias-solar-memory-dev`: Solar Memory mode, journey, map node pools,
  preparation flow, finale, fixed bosses, multiplayer role commit, and sync
  repair.
- `terrias-event-dev`: non-Solar-Memory EventList/Map rows, story chains,
  reward helpers, and ordinary map-visible events.
- `terrias-shared-runtime-dev`: Aura shared runtimes, shared resources, Journey,
  Skin, Audio, BGM, StarterDeck, CG, UI safety, initialization registration,
  tool-local configuration overrides, cross-mod sync models, RPC authority,
  shared DLL packaging, shared release gates, or deciding whether a reusable
  capability belongs in Terrias, AuraToolsExp, or a shared component.
- `terrias-visual-runtime-dev`: `visual.registry.json`, VisualBundles, shaders,
  card visual skins/effects, Skill CG, animated icons, map-node visuals, Star
  Score HUD, Wuna orbit fire, or visual runtime validation.
- `terrias-card-art-style`: card-face art, relic icons, contact sheets, and
  bitmap image asset validation.
- `terrias-skill-evolution`: updating the project-local skills from development
  traces or planned renaming/generalization.

## Workflow

1. For any solution, repair, refactor, migration, or compatibility change, load
   `terrias-complete-solution-gate` before choosing an implementation direction.
2. Inspect the current feature surface before editing:
   - `Terrias-Dev/**/*.cs`
   - `Terrias/Data/**/*.csv`
   - `Terrias/Text/**/*.csv`
   - `Terrias/ModConfig.json`
   - `Terrias/audio.registry.json` when audio, vocal, or BGM behavior changes.
   - `Terrias/visual.registry.json` and `Terrias/SharedResources/*` when runtime
     visuals, Skill CG, or shared resource manifests change.
   - release-facing docs only when behavior, counts, or user-facing claims change.
3. Load only the relevant reference:
   - Card, Buff, Relic, CardPack fields: `references/csv-schema.md`
   - C# boundaries, Managed signature drift, hook containment, multiplayer
     routing, and game-reference index routing:
     `references/csharp-authoring-boundaries.md`
   - Decompiled game reference search index:
     `references/game-reference-index.md`
   - External Unity or mature-project best-practice links:
     `references/external-best-practice-index.md`
   - Role, dialogue, and event expansion: `references/expansion-role-dialogue-event.md`
   - Map-event authoring checklist: `references/solar-event-expansion.md`
   - Validation expectations: `references/validation-rules.md`
   - For C# architecture refactors, also use `terrias-architecture-dev`.
   - For Solar Memory work, also use `terrias-solar-memory-dev`.
   - For shared runtime work, initialization registration, AuraToolsExp local
     config overrides, cross-mod sync, payload guard, timing, or duplicate
     suppression, also use `terrias-shared-runtime-dev`.
   - For runtime visual work, also use `terrias-visual-runtime-dev`.
   - For EventList, Text/EventList, map-visible event, and event helper work, also use the project-local `terrias-event-dev` skill.
4. Keep behavior in C# by default:
   - CSV script columns should call `CS.Terrias.Dll.Scripting.*` entry points.
   - Put card, buff, relic, role, boss, and event behavior in the matching `Terrias-Dev/Scripting/*Scripts.cs` file.
   - Put shared game-facing wrappers in `Terrias-Dev/GameApi/`, reusable
     implementation code in `Terrias-Dev/Mechanics/`, and IDs/utilities in
     `Terrias-Dev/Infrastructure/`.
   - Treat `Terrias-Dev/Mechanics` as a mostly flat service/model directory
     unless the current repository already has a stable sub-domain grouping.
   - Put runtime hook and UI integration code in `Terrias-Dev/Hooks/`.
   - Put feature runtimes that are not CSV entry points under `Terrias-Dev/Features/`.
5. Keep Data and Text rows synchronized. Any new card, buff, relic, card pack, role, dialogue, map, or event needs both config and localized text when the template has both sides.
6. Prefer existing Terrias C# helpers over inline CSV logic. Add a shared helper only when multiple scripts need the same behavior or nil-safe wrapper.
7. Treat the repository `Managed/` assemblies as the current compile contract. Use the decompiled reference to understand behavior, then verify signatures against current assemblies when APIs may have changed.
8. Check authoring boundaries before validation when edits touch new C# entry points, hooks, CSV script columns, resource paths, or localized descriptions.
9. Select validation from `references/validation-rules.md` according to the
   changed contract. Build and test commands that write `Terrias.Aura.dll`
   must be serial. A typical Terrias C# behavior change uses:

```powershell
tools\Build-TerriasDll.ps1
tools\Test-TerriasCSharp.ps1
```

Add architecture, resource, event, visual, shared, network, packaging, or full
release checks only when the impact matrix selects them. Specialist Terrias
profiles are independent; `csharp` does not silently include Columbina,
Elemental, Familiar, or Spirit validation.

## Hard Rules

- For every proposed or implemented solution, obey
  `terrias-complete-solution-gate`; do not retain a temporary or superseded
  operational path as the final state.
- Do not add script implementation paths outside the C# DLL production surface.
- Do not paste official template snippets directly into CSV script columns. CSV script columns should call stable Terrias C# entry points.
- Use full mod IDs when referencing Terrias-defined content.
- Keep player-facing text, dynamic descriptions, release notes, and behavior in sync when rows or scripts change.
- Do not write battle-only or run-only state back into base CSV `Data/*` rows.
- Treat `IDataConfig.data` as read-only host configuration. Write runtime card
  overrides to `Vars`; copy and merge `data` only when composing persistent
  payloads such as `Vars["RawData"]`. See
  `references/csharp-authoring-boundaries.md`.
- Keep historical anchors out of operational skills. Record them only through
  `terrias-skill-evolution` archaeology or staleness notes.
- Do not expose Terrias internal helpers as the development base for AuraToolsExp.
  Tool and content mods must depend on shared/core layers, not on each other.
  Promote shared hook, UI, resource, logging, pooling, or multiplayer
  presentation foundations to Aura shared runtimes instead.
- Do not bind directly to a Managed method whose signature has drifted across supported game versions. Put reflection-based current/legacy dispatch and a deterministic fallback in `GameApi/`.
- Do not let one independent fight-start or lifecycle action abort later actions. Isolate fragile steps and log each failure with its step name.
- For shared multiplayer progression, player-scoped state, presentation events,
  RPC authority, and duplicate suppression, route to
  `terrias-shared-runtime-dev` and its sync scenario reference instead of
  duplicating the protocol rules here.
- Leave `Text/Relic.Tag` blank unless a visible relic label is intentionally needed; it is separate from `Data/Relic.PackBelong`.
- Do not run `tools\Build-TerriasDll.ps1` and `tools\Test-TerriasCSharp.ps1` in parallel; both can write the same DLL output.
- Do not run archived `TestMods` validation for Terrias, AuraToolsExp, Core, or
  shared-runtime changes. Use `tools\Test-TestMods.ps1` only for an explicit
  prototype-maintenance task.
- Retire tests that no longer map to a current behavior, public contract,
  boundary, release artifact, or owned content requirement. Replace brittle
  source snapshots with behavior tests instead of preserving implementation
  history indefinitely.

## Authoring Checks

- CSV script columns should stay short and delegate to `CS.Terrias.Dll.Scripting.*`.
- New public CSV-callable C# methods should have stable names and small parameter lists.
- Hook code should be isolated under `Terrias-Dev/Hooks/` and verified against the decompiled method signature before use.
- Runtime visual declarations should be centralized in `Terrias/visual.registry.json`
  and the visual runtime skill, not hidden in feature-specific hard-coded paths.
- New `MapTree.Node` instances must receive a valid deterministic `NodeDice` before entering map lists or sync arrays.
- Rebuild `Terrias/Scripts/Entry.dll` after every C# compatibility or hook change; source edits alone do not change shipped behavior.
- Data/Text CSV rows, icon paths, and localized descriptions should be updated in the same change.
- Old dynamic helper calls are not allowed in CSV script columns; use C# entry points.

## Useful Commands

Inventory current content:

```powershell
.codex\skills\terrias-mod-dev\scripts\extract-terrias-inventory.ps1
```

Build the C# DLL:

```powershell
tools\Build-TerriasDll.ps1
```

Run C# tests:

```powershell
tools\Test-TerriasCSharp.ps1
```

Run architecture assertions:

```powershell
tools\Test-TerriasArchitecture.ps1
```

List the machine-readable Terrias validation inventory or run a focused
profile:

```powershell
tools\Test-TerriasGate.ps1 -List
tools\Test-TerriasGate.ps1 -Profile elemental
tools\Test-TerriasGate.ps1 -Tag resources
```

Run the comprehensive Terrias gate only for release-level validation:

```powershell
tools\Test-TerriasGate.ps1 -Profile full-release
```

Run Terrias CSV/resource validation:

```powershell
.codex\skills\terrias-mod-dev\scripts\validate-terrias.ps1
```
