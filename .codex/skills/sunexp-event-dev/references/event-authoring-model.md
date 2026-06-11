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

Recommended helpers:

```lua
SunExp_BeginPackEvent(self, step)
SunExp_PackRewardCard(step, "SunExp_sunexp_card_id")
SunExp_PackRewardRelic(step, "SunExp_sunexp_relic_id")
SunExp_PackRewardBless(step, "blessing_id")
SunExp_PackFinish(step)
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
- Prefer compact helper calls in CSV, not long inline logic.
- If multiple event rows share behavior, implement a Lua helper in `_src/events`.

## Text/EventList

Columns to keep aligned:

- `TotalDescribe`
- `1Describe` through `4Describe`

Style:

- Use `<main>` for the visible option title or event body.
- Use `<add>` for mechanical reward text.
- Use `<subtip>` for flavor or risk detail.
- Do not put reward text in Lua captions; event option text is the player-facing explanation.

## Progress

- Store chain progress in a named game var.
- Begin helper should show normal choices only when `currentProgress == step - 1`.
- Repeat helper should be selected only after the chain is complete.
- Reward helper should advance progress to at least the completed step, then end the event.
