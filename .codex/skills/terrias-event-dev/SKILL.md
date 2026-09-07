---
name: terrias-event-dev
description: Author or repair Terrias ordinary events, story chains, map-visible events and their Data/Text/C# alignment. Use the Solar Memory skill for that mode's preparation, fixed bosses, finale and multiplayer role commit.
---

# Terrias Event Dev

Inspect affected rows under Terrias/Data and Terrias/Text, then
Terrias-Dev/Scripting/EventScripts.cs and its owning helpers.

## Event classification

- Ordinary events may enter the native random pool.
- Story chains use Sub_ IDs and explicit progression.
- Map-visible special events need Map rows, runtime selection and narrowly
  scoped selection sync.
- Mode-exclusive content requires a mode-owned guarded factory plus cleanup of
  old nodes/sync arrays outside that mode.
- Repeated events need explicit progress logic.
- Removed events require aligned Data/Text, entry-point and test cleanup.

## Authoring contracts

- CSV scripts call stable C# EventScripts entry points. Keep option behavior
  aligned with localized descriptions and reward/progress helpers.
- Do not rely on UI construction alone for map-visible selection.
- Repair only the special event's mapdata; do not globally rewrite ordinary
  events.
- Breaks_, unreachable Level and Rarity=7 do not establish mode isolation.
  Unknown Text/Map.Note keys may be selected by native weighting and crash.
- Do not restore retired rows merely to satisfy old tests.
- Route mode preparation and finale to
  [Solar Memory](../terrias-solar-memory-dev/SKILL.md); ordinary dialogue,
  rewards and visuals remain in their owning runtimes.

## References and tools

- [Authoring model](references/event-authoring-model.md)
- [Native map selection](references/map-event-runtime.md)
- [Reward helpers](references/reward-helper-patterns.md)
- [CSV alignment](references/csv-sync-checklist.md)
- [Regression boundaries](references/regression-checks.md)
- `tools/Get-TerriasEventChain.ps1 -Prefix <current-prefix>`: inspect a chain.
- `tools/Test-TerriasEvents.ps1`: validate current event content.
- `tools/Test-TerriasContent.ps1`: general Terrias content alignment.

Choose additional checks through the
[impact guide](../aura-project-dev/references/validation.md).
Text-only changes do not require product builds. C# event changes add the
owning behavior suite and one product transaction; native event-data changes
select the events profile.
