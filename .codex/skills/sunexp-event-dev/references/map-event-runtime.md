# Map Event Authoring

Use this reference when a SunExp event needs a visible map node or map-selection
hook.

## Files To Edit

- `SunExp/Data/Map/sunexp.csv`
- `SunExp/Text/Map/sunexp.csv`
- `SunExp/Data/EventList/sunexp.csv`
- `SunExp/Text/EventList/sunexp.csv`
- `SunExp-Dev/Scripting/EventScripts.cs`
- `SunExp-Dev/Hooks/*` when map generation or selection code is needed.

## Authoring Rules

- Keep map rows and map text rows paired by id.
- Keep event rows and event text rows paired by id.
- Put event option behavior behind `CS.SunExp.Dll.Scripting.EventScripts.*`.
- Put map-generation or map-selection hook code under `SunExp-Dev/Hooks/`.
- Verify hook target names and argument shapes in the decompiled reference before editing hook code.
- Avoid broad map rewrites; target only the custom map id or row being authored.

## Engine Pool Behavior

- `MapTree.TypeGenerate(note)` reads the global `DataType.Map` table and filters
  by `Note`. It does not filter `NodeId` values containing `Breaks`.
- Its `Level` filter applies only to native fight notes such as boss, elite, and
  normal combat. Ordinary-event rows ignore `Level`.
- `NormalMapManager.RandomGenerate` has a `Breaks` filter, but fixed map slots
  also call `TypeGenerate`, so `Breaks_` alone cannot isolate a map row.
- Every `RandomPool` constructor removes rows whose `Rarity` is `7`. Use this as
  the internal/fixed-row marker; direct `GameConfigManager.GetOne` still works.
- Keep `Text/Map.Note` on a native supported value. Unknown notes can enter
  weighted draws and then fail when native weight dictionaries index the key.

## Mode-Exclusive Strategy

For content owned by one mode, apply all layers:

1. Give every exclusive event, setup, and boss Map row `Rarity=7`.
2. Keep `Sub_` on story EventList rows so the ordinary event pool cannot draw them.
3. Create fixed nodes through a mode-guarded factory using direct row lookup.
4. Centralize full and short exclusive IDs in `Infrastructure/`.
5. Outside the owning mode, sanitize generated map lists before UI creation.
6. Repair only exclusive entries in multiplayer `maps`/`mapData` arrays.
7. Preserve an already assigned non-exclusive official event `NodeId` when only
   the visual Map template was polluted.
8. Assign deterministic `NodeDice` to every replacement or fixed node.

Use a deterministic native fallback row for old saves. Do not mutate global map
rows and do not generate a replacement independently from unsynchronized random
state on each client.

## Decompiled Reference Searches

```powershell
rg -n "MapSelectUI|NormalMapManager|MapManager|SelectNode" "开发参考资料\反编译文件夹v1.0.23693118"
rg -n "EventList|Choice1|Choice2|EndEvent|ContinueEvent" "开发参考资料\反编译文件夹v1.0.23693118\AllScripts" "开发参考资料\反编译文件夹v1.0.23693118\Witch"
```

## Validation

```powershell
.codex\skills\sunexp-event-dev\scripts\validate-sunexp-events.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```
