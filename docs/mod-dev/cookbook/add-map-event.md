# Add a Map Event

This checklist covers EventList-driven map events in a SunExp-style project.

## 1. Add EventList Data

Add a row under:

```text
<ModName>/Data/EventList/<file>.csv
```

Typical fields:

- `Id`
- `1Script`
- `2Script`
- `3Script`
- `4Script`
- `InitScript`
- `IsHighRisk`
- `EntryScript`

Keep option scripts short:

```csv
CS.SunExp.Dll.Scripting.EventScripts.RewardRelic(1, "SunExp_sunexp_morning_shard");
```

## 2. Add EventList Text

Add a matching row under:

```text
<ModName>/Text/EventList/<file>.csv
```

Fill title, total description, option descriptions, and localized variants.
Option text should exist only for options that can be selected.

## 3. Add Map Row

If the event should appear on the map, add or update:

```text
<ModName>/Data/Map/<file>.csv
<ModName>/Text/Map/<file>.csv
```

Verify `Type`, `NodeId`, and `Level` against existing map rows and decompiled map
flow.

## 4. Add C# Behavior

Use:

```text
<ModName>-Dev/Scripting/EventScripts.cs
```

Use `PlayerApi` or the local equivalent for rewards, captions, game vars, and
event termination.

## 5. Validate Flow

Automated checks should confirm Data/Text sync and basic resource paths. Manual
checks should cover:

- event appears at the intended map layer
- `InitScript` prepares text/vars before the player sees options
- option scripts call `ContinueEvent`, `EndEvent`, or another clear flow action
- repeat events cannot duplicate one-time rewards unless intended
