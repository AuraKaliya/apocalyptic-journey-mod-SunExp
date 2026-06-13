# Validation Rules

Use this reference before finishing any SunExp content change.

## Required automated checks

Run:

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

This checks:

- C# compile and focused SunExp C# regression tests.
- Old dynamic helper calls, inline script blocks, or production `.lua` files.
- Data/Text ID pairing for Card, Buff, Relic, CardPack, RoleData, Dialogue, and EventList when present.
- Data/Text ID pairing for Map when present.
- EventList text shape for `TotalDescribe` and scripted option descriptions.
- `PackBelong` references for cards and relics.
- Mod resource paths that point to missing files.
- Basic `{0}` style placeholder consistency for card descriptions.

## Manual checks

Automated checks do not prove Unity runtime semantics. Manually reason through:

- Does every target effect set the intended status first?
- Does dynamic display match runtime behavior?
- Does every event listener avoid duplicated hooks?
- Does every changed behavior update player-facing text?
- Does any changed hook or game API wrapper need in-game verification?

## Known limitations

The local validation script cannot load the Unity runtime, instantiate `ScriptExecutor`, or prove UI/DLL hook behavior. Treat any UI hook, card-pack selection rule, or Managed-layer behavior as needing in-game verification.
