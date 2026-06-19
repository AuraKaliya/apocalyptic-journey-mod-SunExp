# Map Event Authoring

Use this reference when a SunExp event needs a visible map node or map-selection
hook.

## Files To Edit

- `SunExp/Data/Map/sunexp.csv`
- `SunExp/Text/Map/sunexp.csv`
- `SunExp/Data/EventList/sunexp.csv`
- `SunExp/Text/EventList/sunexp.csv`
- `SunExp-Dev/Scripting/EventScripts.cs`
- `SunExp-Dev/Hooks/*` when map generation or selection code is needed.

## Authoring Rules

- Keep map rows and map text rows paired by id.
- Keep event rows and event text rows paired by id.
- Put event option behavior behind `CS.SunExp.Dll.Scripting.EventScripts.*`.
- Put map-generation or map-selection hook code under `SunExp-Dev/Hooks/`.
- Verify hook target names and argument shapes in the decompiled reference before editing hook code.
- Avoid broad map rewrites; target only the custom map id or row being authored.

## Decompiled Reference Searches

```powershell
rg -n "MapSelectUI|NormalMapManager|MapManager|SelectNode" "开发参考资料\反编译文件夹v1.0.23693118"
rg -n "EventList|Choice1|Choice2|EndEvent|ContinueEvent" "开发参考资料\反编译文件夹v1.0.23693118\AllScripts" "开发参考资料\反编译文件夹v1.0.23693118\Witch"
```

## Validation

```powershell
.codex\skills\sunexp-event-dev\scripts\validate-sunexp-events.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```
