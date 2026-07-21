# Map Event Authoring Checklist

Use this reference when adding or changing Terrias map-visible events or event
chains. Pair it with the `terrias-event-dev` skill for event-specific checks.

## Files To Inspect

- `Terrias/Data/Map/terrias.csv`
- `Terrias/Text/Map/terrias.csv`
- `Terrias/Data/EventList/terrias.csv`
- `Terrias/Text/EventList/terrias.csv`
- `Terrias-Dev/Scripting/EventScripts.cs`
- `Terrias-Dev/Hooks/*` when map generation or selection behavior is involved.

## Authoring Rules

- Keep event CSV scripts as short `CS.Terrias.Dll.Scripting.EventScripts.*` calls.
- Keep Data and Text rows synchronized for every event and map row.
- Use `Sub_` ids for controlled story-chain event rows so they are not ordinary top-level events.
- Use full mod IDs in script arguments when referencing Terrias cards, relics, maps, or events.
- Put shared event behavior in `EventScripts.cs` or a supporting C# helper.
- Put map-generation or selection hooks under `Terrias-Dev/Hooks/` after verifying the target game method in the decompiled reference.

## Game Reference Searches

Load `references/game-reference-index.md` before searching the decompiled game
reference. Use its event and map search routes to confirm method names,
argument shape, and likely hook location. Keep feature-specific findings in the
current task context; record only versioned corrections in the index.

## Validation

```powershell
tools\Build-TerriasDll.ps1
tools\Test-TerriasCSharp.ps1
.codex\skills\terrias-event-dev\scripts\validate-terrias-events.ps1
.codex\skills\terrias-mod-dev\scripts\validate-terrias.ps1
```

Also verify with `Import-Csv` after editing localized event text, especially
when English or Japanese text contains commas.
