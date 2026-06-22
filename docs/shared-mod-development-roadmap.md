# Shared Mod Development Roadmap

This project treats `SunExp`, `SanGuoShaExp`, and `AuraToolsExp` as the main Mods. Mods under `TestMods` are prototypes
and validation consumers.

## Platform baseline

- Resource packages use `SharedResources/package.json` and the schema in
  `AuraSharedCore/Schemas/resource-package.schema.json`.
- Shared diagnostics use `AuraSharedDiagnostics` for service, owner, phase, authority, and correlation fields.
- Build validation runs through `tools/Test-MainSharedFramework.ps1` for main Mods and
  `tools/Build-SharedRuntimeConsumers.ps1` when prototype consumers also need coverage.
- Shared service docs live beside the service folder, with `AuraSharedCore/README.md` describing platform contracts and
  each specialized service documenting its own semantics.

## AuraJourneyShared

`AuraJourneyShared` is the reusable journey layer. It owns definitions, route graphs, native map-node specs, runtime state,
condition evaluation, event history, sync-array projection, game-node construction helpers, and authority-gated commits. It
does not directly patch map, event, or UI methods; owning Mods keep those hooks and call the shared service when they need
shared state or native node projection.

The first production migration target is SunExp Solar Memory. The expected first migration is incremental: register a Solar
Memory journey definition, express fixed slots as `RouteGraph` / `MapNodeSpec`, mirror key route state into
`AuraJourneyState`, then gradually replace local helper code with `AuraJourneyGameBridge` while keeping existing SunExp
map hooks intact.

Shared code must stay framework-only. Concrete journey content, including map IDs, event IDs, boss IDs, localized labels,
story names, and MOD-specific ID aliases, belongs in the owning main Mod. For example, a main Mod may register its own map
ID alias rules through `AuraJourneyMapIdAliasRegistry`, but `AuraJourneyShared` must not hard-code those prefixes.

The current shared boundary intentionally captures the hard-won Solar Memory lessons:

- every custom `MapTree.Node` needs deterministic `NodeDice`
- custom event nodes need `Id`, `Type`, `Note`, `NodeId`, and `Level`
- fixed slots must repair both map UI nodes and `mapList` / `mapData`
- Break nodes need explicit preservation rules
- run state must be tied back to the native mode/save keys rather than only a generic shared document

## Complete role packs

A complete role pack should declare itself as `packageKind: "RolePack"` and use package capabilities such as `Audio`,
`Skin`, `CG`, `Journey`, and `MultiplayerAuthority`. A role pack is considered complete when it can be validated as one
coherent package rather than as unrelated CSV, audio, skin, CG, and hook edits.

The minimum production checklist is:

- role identity and target role ids
- starter deck or card-pack behavior
- audio or BGM resources when applicable
- skin or animation resources when applicable
- CG resources when applicable
- journey or event-chain integration when applicable
- multiplayer authority notes for any shared state

## Multiplayer rule

Only authoritative code may advance shared progression. Client-side hooks may preview, animate, and display state, but
journey progress, rewards, route choices, and team-level event results must be committed through authority-gated APIs.
