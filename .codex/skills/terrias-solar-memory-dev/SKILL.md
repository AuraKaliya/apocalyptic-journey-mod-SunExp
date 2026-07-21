---
name: terrias-solar-memory-dev
description: Project-local skill for designing, debugging, or reviewing Terrias Solar Memory mode, including journey registration, mode entry, map node pools, fixed story events, boss and finale routing, preparation state, custom starter decks, origin and blessing setup UI, multiplayer role commit, map synchronization, old-save migration, and Solar Memory validation in Witch's Apocalyptic Journey.
---

# Terrias Solar Memory Dev

Use this skill inside this repository for Solar Memory mode work. Pair it with
`terrias-mod-dev`; pair it with `terrias-event-dev` only when editing
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
   - `Terrias-Dev/Hooks/SolarMemory*.cs`
   - `Terrias-Dev/Hooks/ModeChoice*.cs`
   - `Terrias-Dev/Hooks/Ui/TerriasModalHost.cs`
   - `Terrias-Dev/Hooks/Ui/TerriasUi*.cs`
   - `Terrias-Dev/GameApi/SolarMemory*.cs`
   - `Terrias-Dev/Mechanics/SolarMemory*.cs`
   - `Terrias-Dev/Mechanics/MapNodeSafetyService.cs`
   - `Terrias-Dev/Infrastructure/TerriasIds.cs`
   - `Terrias-Dev/Network/RpcSolarMemoryRoleCommit.cs`
   - `Terrias/Data/EventList/terrias.csv`, `Terrias/Text/EventList/terrias.csv`
   - `Terrias/Data/Map/terrias.csv`, `Terrias/Text/Map/terrias.csv`
3. Load references as needed:
   - `references/mode-flow.md`: mode choice, run launcher, preparation, event-script facade, finale, and old-save flow.
   - `references/map-node-contract.md`: map row isolation, node generation, `NodeDice`, and sync arrays.
   - `references/multiplayer-role-commit.md`: player-scoped setup state and final authoritative role commit.
   - Use `terrias-visual-runtime-dev` for title art, map-card visuals, or setup-window visual polish.
4. Keep CSV event scripts narrow. `EventScripts` should call
   `SolarMemoryFlowApi` for mode behavior; it must not import `Hooks`.
5. Run Solar Memory validation through the normal Terrias checks before finishing.

## Hard Rules

- Keep Solar Memory-exclusive EventList rows as `Sub_` rows.
- Keep Solar Memory-exclusive Map rows, setup events, and bosses out of global
  pools through mode-owned factories, runtime guards, and sanitizers; do not
  assume `Rarity=7` is a safe or sufficient isolation mechanism.
- Centralize exclusive id detection in `TerriasIds`.
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
- Use `TerriasModalHost`, `TerriasUiSafety`, `TerriasUiPool`, `TerriasUiSprites`,
  and `TerriasUiBuilder` for transient setup UI, pooling, cached sprites, and
  teardown.

## Validation

Run build and tests serially because build outputs share `Terrias.Aura.dll`:

```powershell
tools\Build-TerriasDll.ps1
tools\Test-TerriasArchitecture.ps1
tools\Test-TerriasCSharp.ps1
.codex\skills\terrias-event-dev\scripts\validate-terrias-events.ps1
.codex\skills\terrias-mod-dev\scripts\validate-terrias.ps1
```

When a task touches shared Journey, StarterDeck, audio, skin, or shared package
behavior, also use `terrias-shared-runtime-dev`.
