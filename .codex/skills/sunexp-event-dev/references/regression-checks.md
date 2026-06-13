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
