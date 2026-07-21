# Solar Memory Map Node Contract

Use this reference when editing Map rows, map pools, fixed story nodes,
selection arrays, map item animation fallback, or node safety.

## Isolation

Solar Memory-exclusive content must not leak into base map generation. Do not
rely on `Rarity=7`, `Breaks_`, or unreachable `Level` values as the isolation
contract. Retain mode guards, build fixed nodes through the owning mode's
factory, and sanitize generated nodes in non-Solar-Memory modes.

Centralize exclusive checks in `TerriasIds.IsSolarMemoryExclusiveMapId` and
`TerriasIds.IsSolarMemoryExclusiveEventId`.

## Node Generation

Use `SolarMemoryMapNodePoolFactory` for current-layer default and selectable
nodes. Use `SolarMemoryMapNodePoolApplier` to apply the pool to the current
layer segment, not only layer zero.

Fixed nodes should be defined from stable arrays or specs so sync repair,
localization, and event ids stay aligned:

- fixed story event ids;
- fixed map ids;
- layer names;
- fixed boss level ids.

Do not expose fixed story events as draggable SelectNode candidates. Reserve the
fixed slot through runtime specs.

## NodeDice

Every custom, replacement, fallback, or restored `MapTree.Node` must receive a
deterministic `NodeDice` before entering `DefaultNode`, `SelectNode`,
`currentNode`, or sync arrays. Prefer the owning tree dice cursor; use
`Dice.Default` only for fixed nodes that do not draw.

Use `MapNodeSafetyService.EnsureNodeDice` rather than setting dice ad hoc.

## Multiplayer Sync Arrays

When fixed Solar Memory nodes are involved, repair both:

- the authoritative `MapTree` node lists;
- synchronized `maps` and `mapData` arrays.

Client-only current-node restoration must be gated so clients do not advance
host authority. Prefer synchronized map arrays when restoring `currentNode`, and
persist restored nodes through `GameSaveManager.UpdateNode`.

## Native Map Item Timing

Do not rewrite map node pools immediately before native `MapItemInit` consumes
default nodes. If a fixed boss map item needs an animation path that native code
expects from enemy rows, use the existing before/after animation fallback that
temporarily replaces and restores the row.
