# Reward Helper Patterns

Use reward helpers to keep EventList CSV rows compact and consistent.

## Helper Responsibilities

A reward helper may:

- Grant gold.
- Grant a card, relic, or blessing.
- Advance event-chain progress.
- End the event.

A reward helper should not:

- Show hard-coded English captions.
- Store player-facing text.
- Inline language-specific UI strings.
- Mutate unrelated event state.

## Pattern

```lua
function SunExp_PackRewardRelic(progress, relicId)
    SunExp_GainGold(100)
    SunExp_AddRelicReward(relicId)
    SunExp_PackFinish(progress)
end
```

Use matching helpers for cards and blessings.

## Progress Finish

```lua
function SunExp_PackFinish(progress)
    SunExp_AdvancePackEvent(progress)
    SunExp_EndEvent()
end
```

Use `math.max(current, progress)` semantics so repeated calls cannot reduce progress.

## Registration

Every helper called by CSV must be registered in:

```text
SunExp/Scripts/_src/registry.lua
```

Then rebuild:

```powershell
tools\Build-SunExpEntry.ps1
```
