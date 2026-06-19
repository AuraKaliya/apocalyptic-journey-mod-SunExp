---
name: sunexp-event-dev
description: >-
  Project-local skill for designing, adding, reviewing, or fixing SunExp
  EventList rows, Text/EventList localization, card-pack story event chains,
  map-visible or mode-exclusive events and bosses, random-pool isolation,
  multiplayer map synchronization, event reward/progress behavior, and C#
  event scripts for Witch's Apocalyptic Journey.
---

# SunExp Event Dev

Use this skill inside the SunExp repository for event work. Pair it with
`sunexp-mod-dev`; this skill only adds event-specific guardrails.

## Workflow

1. Classify the event surface:
   - Ordinary event: may enter the base random event pool.
   - Story chain event: use `Sub_` ids so it is not randomly drawn as an ordinary event.
   - Map-visible special event: needs `Data/Map`, `Text/Map`, runtime selection handling, and narrow selection sync repair.
   - Mode-exclusive event or boss: must be excluded from every global map pool and admitted only by that mode's guarded factory.
   - Repeat event: must be entered only by explicit progress logic.
2. Inspect the current rows and C# behavior before editing:
   - `SunExp/Data/EventList/sunexp.csv`
   - `SunExp/Text/EventList/sunexp.csv`
   - `SunExp/Data/Map/sunexp.csv`
   - `SunExp/Text/Map/sunexp.csv`
   - `SunExp-Dev/Scripting/EventScripts.cs`
   - related helpers under `SunExp-Dev/GameApi/`, `SunExp-Dev/Mechanics/`, and `SunExp-Dev/Infrastructure/`.
3. Add or update Data rows and matching Text rows together.
4. Keep event behavior in C# by default. CSV script columns should call stable `CS.SunExp.Dll.Scripting.EventScripts.*` entry points.
5. Keep event option scripts aligned with localized option descriptions.
6. Run event validation and full SunExp validation.

## Hard Rules

- Event behavior must use C# entry points, normally `CS.SunExp.Dll.Scripting.EventScripts.*`.
- Do not make story-chain events top-level ordinary events; use `Sub_`.
- Do not use hard-coded English captions in event reward/progress helpers.
- Do not rely on UI creation alone for map-visible events; keep runtime selection behavior aligned.
- Do not globally rewrite ordinary event `mapdata`; repair only entries whose map id is your special event id.
- Do not treat `Breaks_` or an unreachable `Level` as complete mode isolation. Use `Rarity=7` for internal fixed Map rows, retain a mode guard, and sanitize old generated nodes/sync arrays outside the owning mode.
- Do not invent a custom `Text/Map.Note` merely to isolate content. Native map weighting expects known Note keys and may select unknown keys before crashing.
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
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```
