# Map Event Authoring Checklist

Use this reference when adding or changing SunExp map-visible events or event
chains. Pair it with the `sunexp-event-dev` skill for event-specific checks.

## Files To Inspect

- `SunExp/Data/Map/sunexp.csv`
- `SunExp/Text/Map/sunexp.csv`
- `SunExp/Data/EventList/sunexp.csv`
- `SunExp/Text/EventList/sunexp.csv`
- `SunExp/Dev/Scripting/EventScripts.cs`
- `SunExp/Dev/Hooks/*` when map generation or selection behavior is involved.

## Authoring Rules

- Keep event CSV scripts as short `CS.SunExp.Dll.Scripting.EventScripts.*` calls.
- Keep Data and Text rows synchronized for every event and map row.
- Use `Sub_` ids for controlled story-chain event rows so they are not ordinary top-level events.
- Use full mod IDs in script arguments when referencing SunExp cards, relics, maps, or events.
- Put shared event behavior in `EventScripts.cs` or a supporting C# helper.
- Put map-generation or selection hooks under `SunExp/Dev/Hooks/` after verifying the target game method in the decompiled reference.

## Decompiled Reference Searches

```powershell
rg -n "EventList|Choice1|Choice2|EndEvent|ContinueEvent" "开发参考资料\反编译文件夹\AllScripts" "开发参考资料\反编译文件夹\Witch"
rg -n "MapSelectUI|NormalMapManager|MapManager|SelectNode" "开发参考资料\反编译文件夹"
```

Use the results to confirm method names, argument shape, and likely hook
location. Keep those findings in the current task context instead of adding
feature notes to the skill.

## Validation

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-event-dev\scripts\validate-sunexp-events.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

Also verify with `Import-Csv` after editing localized event text, especially
when English or Japanese text contains commas.
