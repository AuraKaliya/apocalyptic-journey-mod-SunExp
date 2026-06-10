# Validation Rules

Use this reference before finishing any SunExp content change.

## Required automated checks

Run:

```powershell
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

This checks:

- Lua syntax for CSV script snippets and `Scripts/*.lua`.
- C# syntax residue in script columns.
- Data/Text ID pairing for Card, Buff, Relic, CardPack, RoleData, Dialogue, and EventList when present.
- `PackBelong` references for cards and relics.
- Mod resource paths that point to missing files.
- Basic `{0}` style placeholder consistency for card descriptions.

## Manual checks

Automated Lua syntax checks do not prove runtime semantics. Manually reason through:

- Does every target effect set the intended status first?
- Does dynamic display match runtime behavior?
- Does every event listener avoid duplicated hooks?
- Does every changed behavior update player-facing text?
- Does any official C# example need deeper translation than simple syntax changes?

## Known limitations

The local validation script cannot load the Unity runtime, instantiate `ScriptExecutor`, or prove UI/DLL hook behavior. Treat any UI hook, card-pack selection rule, or Managed-layer behavior as needing in-game verification.
