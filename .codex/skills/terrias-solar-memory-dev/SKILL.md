---
name: terrias-solar-memory-dev
description: Develop Solar Memory mode entry, preparation, map routing, fixed bosses, finale, old-save settlement and multiplayer role commit in Terrias. Use the event skill for ordinary story events and the content skill for other modes.
---

# Terrias Solar Memory Dev

Solar Memory is a guarded mode with preparation, exclusive map content and
player-scoped role commit. Inspect only the affected Hooks, GameApi, Mechanics,
Network and Data/Text surfaces.

## References

- [Mode flow](references/mode-flow.md): launcher, preparation, finale and saves.
- [Map contract](references/map-node-contract.md): exclusive IDs, NodeDice and
  map/sync repair.
- [Role commit](references/multiplayer-role-commit.md): final prepared role and
  intermediate sync suppression.

Use [events](../terrias-event-dev/SKILL.md) when EventList/Map rows change,
[shared runtime](../aura-shared-runtime-dev/SKILL.md) when Journey or starter
deck contracts change, and
[visual runtime](../aura-visual-runtime-dev/SKILL.md) for runtime presentation.

## Invariants

- Exclusive EventList rows use Sub_. Exclusive map content enters only through
  mode-owned factories, runtime guards and sanitizers; Rarity=7 is insufficient.
- Centralize exclusive identity in TerriasIds. Clone mutable native map data
  and restore temporary changes after use.
- Custom/restored MapTree.Node instances need deterministic NodeDice.
  Repair both MapTree and multiplayer maps/mapData arrays.
- Fixed completion currently settles after the third layer. Do not rewrite
  nodes immediately before native MapItemInit or add a separate finale layer
  without deliberately changing and validating that routing contract.
- ModeChoiceEntryRegistry owns custom entries and ModeChoiceLayoutRuntime owns
  layout. Do not occupy a native mode slot.
- SolarMemoryRunLauncher owns save creation and preparation initialization;
  EventScripts calls SolarMemoryFlowApi rather than importing Hooks.
- Keep preparation player-scoped. Suppress intermediate role sync and submit
  only the final prepared role through SolarMemoryRoleCommitApi.
- Do not migrate legacy global preparation values during multiplayer.
- Use the established Terrias modal, safety, pool, sprite and UI builder
  runtimes for transient preparation UI and cleanup.

## Validation

Select checks from the
[impact guide](../aura-project-dev/references/validation.md).
Map/event content needs content validation; behavior/hook changes need the
owning C# tests and architecture checks. Build the products once when
publishing C# changes.

For preparation or role-commit changes verify host/client choices remain
independent, only the final role commits, leaving/reopening clears transient
state, and old-save handling does not import another player's setup.
