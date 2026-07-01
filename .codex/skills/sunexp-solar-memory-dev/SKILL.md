---
name: sunexp-solar-memory-dev
description: Project-local skill for designing, debugging, or reviewing SunExp Solar Memory mode, including journey registration, mode entry, map node pools, fixed story events, boss and finale routing, preparation state, custom starter decks, origin and blessing setup UI, multiplayer role commit, map synchronization, old-save migration, and Solar Memory validation in Witch's Apocalyptic Journey.
---

# SunExp Solar Memory Dev

Use this skill inside this repository for Solar Memory mode work. Pair it with
`sunexp-mod-dev`; pair it with `sunexp-event-dev` only when editing
`Data/EventList`, `Text/EventList`, `Data/Map`, or `Text/Map` rows.

Solar Memory is a mode-scale subsystem, not an ordinary map event. Treat it as
a guarded route with its own preparation flow, map rewrite contract, exclusive
content isolation, and multiplayer role commit path.

## Workflow

1. Classify the touched surface:
   - Mode entry or run launch.
   - Mode-choice registration, custom entry layout, or title art.
   - Journey registration or route graph.
   - Preparation flow: starter deck, origin allocation, blessing picker.
   - Map node pool, fixed story node, boss node, or sync repair.
   - Solar finale, hidden boss, fight abort, loss, or old-save settlement.
   - UI cleanup or title art.
   - Multiplayer role submission or player-scoped setup state.
2. Inspect only the relevant code and data before editing:
   - `SunExp-Dev/Hooks/SolarMemory*.cs`
   - `SunExp-Dev/Hooks/ModeChoice*.cs`
   - `SunExp-Dev/Hooks/Ui/SunExpModalHost.cs`
   - `SunExp-Dev/Hooks/Ui/SunExpUi*.cs`
   - `SunExp-Dev/GameApi/SolarMemory*.cs`
   - `SunExp-Dev/Mechanics/SolarMemory*.cs`
   - `SunExp-Dev/Mechanics/MapNodeSafetyService.cs`
   - `SunExp-Dev/Infrastructure/SunExpIds.cs`
   - `SunExp-Dev/Network/RpcSolarMemoryRoleCommit.cs`
   - `SunExp/Data/EventList/sunexp.csv`, `SunExp/Text/EventList/sunexp.csv`
   - `SunExp/Data/Map/sunexp.csv`, `SunExp/Text/Map/sunexp.csv`
3. Load references as needed:
   - `references/mode-flow.md`: mode choice, run launcher, preparation, event-script facade, finale, and old-save flow.
   - `references/map-node-contract.md`: map row isolation, node generation, `NodeDice`, and sync arrays.
   - `references/multiplayer-role-commit.md`: player-scoped setup state and final authoritative role commit.
   - Use `sunexp-visual-runtime-dev` for title art, map-card visuals, or setup-window visual polish.
4. Keep CSV event scripts narrow. `EventScripts` should call
   `SolarMemoryFlowApi` for mode behavior; it must not import `Hooks`.
5. Run Solar Memory validation through the normal SunExp checks before finishing.

## Hard Rules

- Keep Solar Memory-exclusive EventList rows as `Sub_` rows.
- Keep every Solar Memory-exclusive Map row, setup event, and boss hidden from
  global random pools; use `Rarity=7` plus runtime guards and sanitizers.
- Centralize exclusive id detection in `SunExpIds`.
- Do not mutate global map rows for fallback behavior. Clone dictionaries or
  restore temporary native row changes immediately after use.
- Ensure every custom or restored `MapTree.Node` has deterministic `NodeDice`.
- Repair both `MapTree` and multiplayer `maps`/`mapData` arrays when fixed
  Solar Memory nodes are involved.
- Do not rewrite Solar Memory map nodes immediately before native
  `MapItemInit`; fixed completion currently settles after the third layer.
- Do not create a separate finale map layer unless the routing model is
  intentionally redesigned with new tests.
- Keep custom mode entry registration in `ModeChoiceEntryRegistry` and layout
  in `ModeChoiceLayoutRuntime`; do not let Solar Memory occupy a native mode
  slot such as `StoryMode`.
- Keep run save creation and preparation initialization in
  `SolarMemoryRunLauncher`; do not move it back into `SolarMemoryModeRuntime`.
- Keep preparation choices player-scoped. Suppress intermediate role sync and
  submit only the final prepared role through `SolarMemoryRoleCommitApi`.
- Do not migrate legacy global preparation values during multiplayer.
- Use `SunExpModalHost`, `SunExpUiSafety`, `SunExpUiPool`, `SunExpUiSprites`,
  and `SunExpUiBuilder` for transient setup UI, pooling, cached sprites, and
  teardown.

## Validation

Run build and tests serially because build outputs share `SunExp.Aura.dll`:

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpArchitecture.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-event-dev\scripts\validate-sunexp-events.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

When a task touches shared Journey, StarterDeck, audio, skin, or shared package
behavior, also use `sunexp-shared-runtime-dev`.
