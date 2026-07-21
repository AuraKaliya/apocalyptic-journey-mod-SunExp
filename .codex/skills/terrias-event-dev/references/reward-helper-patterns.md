# Reward Helper Patterns

Use reward helpers to keep EventList CSV rows compact and consistent.

## Helper Responsibilities

A reward helper may:

- Grant gold.
- Grant a card, relic, or blessing.
- Update event-chain state when the row requires it.
- End the event.

A reward helper should not:

- Show hard-coded English captions.
- Store player-facing text.
- Inline language-specific UI strings.
- Mutate unrelated event state.

## Pattern

```csharp
public static void RewardRelic(int progress, string relicId)
{
    PlayerApi.AddMoney(100);
    PlayerApi.AddRelic(relicId);
    Finish(progress);
}
```

Use matching helpers for cards and blessings.

## Finish Pattern

```csharp
private static void Finish(int progress)
{
    Advance(progress);
    PlayerApi.EndEvent();
}
```

Keep state updates and event ending in one C# helper when multiple rows share
the same shape.

## CSV Calls

Every helper called by CSV should be exposed as a stable public static C# entry point:

```text
CS.Terrias.Dll.Scripting.EventScripts.RewardRelic(1, "Terrias_terrias_morning_shard");
```

Then rebuild and test:

```powershell
tools\Build-TerriasDll.ps1
tools\Test-TerriasCSharp.ps1
```
