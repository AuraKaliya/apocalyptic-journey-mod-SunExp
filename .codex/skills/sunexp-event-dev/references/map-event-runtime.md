# Map Event Runtime

Use this reference for map-visible special events such as the Solar Event.

## Engine Path

The stable path is:

1. `NormalMapManager.RandomGenerate()` populates `MapTree.SelectNode`.
2. For every generated `Type == "Event"` node, the base game may replace `NodeId` with a random ordinary event id.
3. `MapSelectUI.ReadyToSelect()` takes the current layer range from `MapTree.SelectNode`.
4. `MapSelectUI.CreateMapItem(range)` displays cards for that temporary range.
5. The player places cards into path nodes.
6. `MapSelectUI.SetNodes()` writes placed `MapItem.node.data` into the real path nodes.
7. `MapManager.CmdSelectMap*` syncs `maps[]` and `mapdata[]`.
8. Final load uses the selected node's `Type` and `NodeId`.

## Stable Strategy

For a map-visible special event:

- Modify the real `MapTree.SelectNode` current layer segment.
- Replace one node inside the current layer range.
- Do not append to `SelectNode`; the engine uses fixed layer ranges.
- Set the final event `NodeId` after random generation.
- Keep a narrow sync repair for `maps[i] == special_map_id` only.

## Avoid

- Do not rely on `CreateMapItem` alone. It receives a temporary visible list and may not represent the final selected path nodes.
- Do not globally rewrite all `Event` nodes; fixed story events can be corrupted.
- Do not hook `Commands.load` as a broad repair. By that stage the map id context may be lost.

## Current Solar Pattern

Current Solar Event behavior follows this shape:

- `NormalMapManager.RandomGenerate` after hook repairs the generated tree.
- `NormalMapManager.GeneratrMap` after hook repairs the generated tree.
- `MapSelectUI.ReadyToSelect` before hook repairs the tree before the visible range is read.
- `CmdSelectMap*`, `TargetUpdateMap`, and `RpcUpdateMap` hooks only repair entries whose map id is the Solar Event map id.
