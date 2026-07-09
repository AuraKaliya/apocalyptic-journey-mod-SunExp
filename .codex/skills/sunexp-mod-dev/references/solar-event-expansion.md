# Map Event Authoring Checklist

Use this reference when adding or changing SunExp map-visible events or event
chains. Pair it with the `sunexp-event-dev` skill for event-specific checks.

## Files To Inspect

- `SunExp/Data/Map/sunexp.csv`
- `SunExp/Text/Map/sunexp.csv`
- `SunExp/Data/EventList/sunexp.csv`
- `SunExp/Text/EventList/sunexp.csv`
- `SunExp-Dev/Scripting/EventScripts.cs`
- `SunExp-Dev/Hooks/*` when map generation or selection behavior is involved.

## Authoring Rules

- Keep event CSV scripts as short `CS.SunExp.Dll.Scripting.EventScripts.*` calls.
- Keep Data and Text rows synchronized for every event and map row.
- Use `Sub_` ids for controlled story-chain event rows so they are not ordinary top-level events.
- Use full mod IDs in script arguments when referencing SunExp cards, relics, maps, or events.
- Put shared event behavior in `EventScripts.cs` or a supporting C# helper.
- Put map-generation or selection hooks under `SunExp-Dev/Hooks/` after verifying the target game method in the decompiled reference.

## Game Reference Searches

Load `references/game-reference-index.md` before searching the decompiled game
reference. Use its event and map search routes to confirm method names,
argument shape, and likely hook location. Keep feature-specific findings in the
current task context; record only versioned corrections in the index.

## Validation

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-event-dev\scripts\validate-sunexp-events.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

Also verify with `Import-Csv` after editing localized event text, especially
when English or Japanese text contains commas.
