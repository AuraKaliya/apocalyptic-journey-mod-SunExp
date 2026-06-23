# AuraJourneyShared v1

AuraJourneyShared is the shared journey, route, event-chain, and multiplayer authority layer for the main Mods. It is a
protocol and state service first; game-specific hooks stay in the owning Mod until a behavior proves reusable.

## Scope

- Journey definitions are shared configuration documents under the `Journey` system.
- Journey runtime state is rebuildable runtime storage under the same system.
- Only the authoritative side may commit state changes. Clients can read state and update presentation, but they must not
  advance shared journey progress directly.
- Conditions and reducers are pure C# services so they can be tested without the game runtime.

## Core concepts

- `AuraJourneyDefinition`: stable journey metadata, entry node, tags, and node definitions.
- `AuraJourneyRouteGraph`: reusable route-layer and fixed-slot description.
- `AuraJourneyNodeDefinition`: route or event node metadata plus conditions.
- `AuraJourneyMapNodeSpec`: native map node contract for custom map/event/fight nodes. It carries `mapId`, fallback map id,
  `nodeId`, `type`, `note`, `level`, and `dicePolicy` so custom nodes do not forget the fields the game map flow expects.
- `AuraJourneyCondition`: reusable predicates for flags, values, counters, roles, and player count.
- `AuraJourneyState`: current active node, selected routes, completed nodes, flags, counters, values, and a bounded event
  history.
- `AuraJourneyCommitRequest`: an authoritative mutation request.
- `AuraJourneySyncProjection`: pure repair logic for `mapList` / `mapData` arrays, including fixed slots and Break-node
  preservation.
- `AuraJourneyGameBridge`: game-facing helpers that turn `AuraJourneyMapNodeSpec` into `MapTree.Node`, repair missing
  `NodeDice`, rebuild a current-node chain from sync arrays, and update the save node when requested.

## Intended integration path

1. A main Mod calls `AuraJourneyRuntime.Initialize(modConfig, ownerModId)` during initialization.
2. The Mod registers one or more journey definitions with `RegisterJourney`.
3. Existing gameplay hooks decide when to call `TryCommit` from the authoritative side.
4. UI, CG, BGM, map animation, and player-facing text read state and react locally.

Main Mods should register their own journey definitions and content-specific ID aliases from their own `GameApi` or
`Mechanics` code. AuraJourneyShared should not contain concrete card, event, boss, role, map, or story IDs.

## Journey Identity

`JourneyId` is a shared technical identity. New definitions should author it as:

```text
ownerModId:localJourneyId
```

Examples:

```text
SunExp:solar-memory
SanGuoShaExp:lord-trial
```

Short ids such as `solar-memory` are accepted only as compatibility input.
`AuraJourneyRuntime.RegisterJourney` and `TryCommit` normalize short ids through
`AuraJourneyRuntime.QualifyJourneyId(ownerModId, journeyId)` before writing
shared definition, registry, or runtime state files. Reads try the normalized
owner-qualified file first, then fall back to the legacy short-id file without
deleting or rewriting it.

This keeps two Mods from colliding on the same definition/state file while
preserving older data. If a tool or another Mod needs to read a journey owned by
a different Mod, pass the owner-qualified id explicitly.

## Game-Body Extension Boundary

AuraJourneyShared now owns the reusable contract for custom route nodes, but the owning Mod still owns concrete hook timing.
This split is intentional:

- Shared layer: node specs, route graph, state, conditions, map row projection, sync-array repair, node construction helpers.
- Owning Mod: selecting hook points such as map generation, map selection UI, battle settlement, and event UI transitions.

For custom event nodes, always declare a `MapNodeSpec` rather than only a generic journey node. The projection layer ensures
that `Id`, `Type`, `Note`, `NodeId`, and `Level` are present. The game bridge ensures `NodeDice` is assigned using either
the owning tree dice or `Dice.Default`.

This preserves the lessons from Solar Memory: custom nodes must be deterministic, must survive map sync arrays, must not
pollute unrelated card packs or event records, and must be repaired from host/client sync data when the local `currentNode`
is missing.

## Shared vs Content Boundary

AuraJourneyShared may know how the game executes a map node, but it must not know what any main Mod is trying to say with
that node. Keep the boundary this way:

- Shared: route graph structures, node specs, projection rules, sync-array repair, dice policies, storage, diagnostics.
- Main Mod: concrete map IDs, event IDs, boss IDs, story labels, localized text, balance numbers, and hook orchestration.

When a main Mod uses full IDs that need to resolve to short native table IDs, register the rule with
`AuraJourneyMapIdAliasRegistry.RegisterPrefixAlias` from that Mod. Do not hard-code main-Mod prefixes in shared code.
