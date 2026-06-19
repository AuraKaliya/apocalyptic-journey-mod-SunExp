# Regression Checks

Add or update tests for these risks when the touched event surface requires them.

## Event Chain

- New C# event entry points are defined.
- CSV calls target existing `CS.SunExp.Dll.Scripting.EventScripts.*` methods.
- `Entry.dll` is rebuilt after C# changes.
- Story-chain ids use `Sub_`.
- Repeat events appear only after the required progress.

## Rewards

- Reward helper grants the intended reward.
- Reward helper advances progress.
- Reward helper ends the event.
- Reward helper does not show hard-coded English captions.

## Text

- Data and Text rows have matching ids.
- Each scripted option has a visible option description.
- Reward text in option descriptions matches the actual helper call.

## Map-Visible Events

- Current layer contains exactly one special event node when that is the design.
- The hook replaces a node in the current layer range instead of appending.
- Final `NodeId` points to the controlled event row, not a random ordinary event.
- Selection sync repairs only entries whose `maps[i]` is the special map id.
- Fixed story events remain unchanged.

## Mode Isolation

- Every exclusive Map row, including setup events and bosses, has `Rarity=7`.
- Full and short Map IDs are recognized by one centralized predicate.
- Story EventList IDs remain `Sub_` rows.
- The owning mode bypasses the non-owner sanitizer.
- World Simulation, Sublimation, tutorial, and slot-style modes cannot draw exclusive rows.
- Existing polluted map lists are repaired before map UI creation.
- Multiplayer sync repair changes only exclusive map/event entries.
- Official event `NodeId` values survive visual-template replacement.
- Custom and replacement nodes have deterministic `NodeDice`.
- Tests do not claim that `Breaks_` alone excludes `TypeGenerate` draws.
