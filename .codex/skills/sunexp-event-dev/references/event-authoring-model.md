# Event Authoring Model

Use this reference when adding or reviewing SunExp event chains.

## Event Types

- Ordinary event: a normal top-level event row. It can be drawn by the base random event pool.
- Story chain event: a controlled sequence. Use ids like `Sub_pack_event_01` so the ordinary event pool does not draw later chapters.
- Map-visible special event: a map card that displays as a custom event and enters a controlled `EventList` row.
- Repeat event: an explicit post-chain event, usually `Sub_pack_event_repeat`.

## Naming

Recommended chain ids:

```text
Sub_pack_event_01
Sub_pack_event_02
Sub_pack_event_03
Sub_pack_event_repeat
```

Recommended CSV script calls:

```text
CS.SunExp.Dll.Scripting.EventScripts.Init(self, "Sub_pack_event_01");
CS.SunExp.Dll.Scripting.EventScripts.RewardCard(1, "SunExp_sunexp_card_id");
CS.SunExp.Dll.Scripting.EventScripts.RewardRelic(1, "SunExp_sunexp_relic_id");
CS.SunExp.Dll.Scripting.EventScripts.RewardBless(1, "blessing_id");
```

## Data/EventList

Columns:

- `Id`
- `1Script` through `4Script`
- `InitScript`
- `IsHighRisk`
- `EntryScript`

Rules:

- Put option rewards in `1Script` through `4Script`.
- Put choice visibility and entry gating in `InitScript`.
- Prefer compact C# entry-point calls in CSV, not long inline logic.
- If multiple event rows share behavior, implement or reuse a helper in `SunExp/Dev/Scripting/EventScripts.cs` or the relevant C# support layer.

## Text/EventList

Columns to keep aligned:

- `TotalDescribe`
- `1Describe` through `4Describe`

Style:

- Use `<main>` for the visible option title or event body.
- Use `<add>` for reward or result text.
- Use `<subtip>` for flavor or risk detail.
- Do not put reward text in hard-coded captions; event option text is the player-facing explanation.

## C# Placement

- Put shared event option behavior in `SunExp/Dev/Scripting/EventScripts.cs`.
- Put repeated player/game-var access behind `SunExp/Dev/GameApi/PlayerApi.cs`.
- Check official event script examples in `开发参考资料/反编译文件夹/AllScripts/AllScripts.cs` when the CSV field or API shape is unclear.
