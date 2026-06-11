---
name: sunexp-event-dev
description: >-
  Project-local skill for designing, adding, reviewing, or fixing SunExp
  EventList rows, Text/EventList localization, card-pack story event chains,
  map-visible special events, event reward/progress helpers, and Lua event
  runtime hooks for Witch's Apocalyptic Journey.
---

# SunExp Event Dev

Use this skill inside the SunExp repository for event work. Pair it with
`sunexp-mod-dev`; this skill only adds event-specific guardrails.

## Workflow

1. Classify the event surface:
   - Ordinary event: may enter the base random event pool.
   - Story chain event: use `Sub_` ids so it is not randomly drawn as an ordinary event.
   - Map-visible special event: needs `Data/Map`, `Text/Map`, real `MapTree.SelectNode` handling, and narrow selection sync repair.
   - Repeat event: must be entered only by explicit progress logic.
2. Inspect the current rows before editing:
   - `SunExp/Data/EventList/sunexp.csv`
   - `SunExp/Text/EventList/sunexp.csv`
   - `SunExp/Data/Map/sunexp.csv`
   - `SunExp/Text/Map/sunexp.csv`
   - `SunExp/Scripts/_src/events/*.lua`
   - `SunExp/Scripts/_src/registry.lua`
   - `SunExp/Scripts/_src/setup.lua`
3. Add or update Data rows and matching Text rows together.
4. Use Lua helpers for repeated reward/progress/end-event behavior.
5. Register every new `SunExp_` helper in `registry.lua`.
6. If `_src` changes, rebuild `SunExp/Scripts/Entry.lua`.
7. Run event validation and full SunExp validation.

## Hard Rules

- Write Lua in CSV script columns; do not paste C# snippets from official data.
- Do not use hard-coded English captions in event reward helpers.
- Do not make story-chain events top-level ordinary events; use `Sub_`.
- Do not rely on `MapSelectUI.CreateMapItem` alone for map-visible events.
- Do not append to `MapTree.SelectNode` when the engine expects fixed layer ranges.
- Do not globally rewrite ordinary event `mapdata`; repair only entries whose map id is your special event id.
- Keep `Data/EventList` option scripts aligned with `Text/EventList` option descriptions.

## References

- `references/event-authoring-model.md`: event ids, chain structure, Data/Text row shape.
- `references/map-event-runtime.md`: current engine behavior and stable map-visible event strategy.
- `references/reward-helper-patterns.md`: reward helper responsibilities and anti-patterns.
- `references/csv-sync-checklist.md`: CSV alignment and quoting checks.
- `references/regression-checks.md`: tests that should be added for fragile event work.

## Useful Commands

Inspect an event chain:

```powershell
.codex\skills\sunexp-event-dev\scripts\inspect-event-chain.ps1 -Prefix Sub_wuna_event
```

Run event-specific validation:

```powershell
.codex\skills\sunexp-event-dev\scripts\validate-sunexp-events.ps1
```

Run full validation:

```powershell
tools\Build-SunExpEntry.ps1
tools\Test-SunExpEntryLoad.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```
