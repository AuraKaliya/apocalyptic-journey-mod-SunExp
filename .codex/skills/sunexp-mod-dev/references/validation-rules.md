# Validation Rules

Use this reference before finishing any SunExp content change.

## Required automated checks

Run the base chain serially:

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpArchitecture.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

Run these serially. `Build-SunExpDll.ps1` and `Test-SunExpCSharp.ps1` can both
write `SunExp-Dev/obj/Release/net472/SunExp.Aura.dll` and should not be
parallelized.

Add scenario checks as needed:

```powershell
.codex\skills\sunexp-event-dev\scripts\validate-sunexp-events.ps1 # EventList or Map
tools\Build-SunExpVisualBundle.ps1 # VisualAssets, shaders, bundled CG, or VisualBundles
tools\Test-NetworkRpcAuthority.ps1 # SunExp or AuraTools server-bound RPC authority
tools\Test-SharedArchitectureGuidelines.ps1 # shared runtime contract or docs
tools\Test-AuraSharedCore.ps1 # AuraSharedCore or shared protocol
tools\Test-SharedReleaseGate.ps1 # broad shared release compatibility
tools\Test-SharedDllPackaging.ps1 # packaged Aura.Shared.dll references or hashes
tools\Build-AuraToolsExpDll.ps1 # AuraTools shared consumer or Skill CG tool changes
.codex\skills\sunexp-skill-evolution\scripts\audit-sunexp-skill-staleness.ps1 # skill or architecture-boundary updates
```

This checks:

- C# compile, architecture assertions, and focused SunExp C# regression tests.
- Old dynamic helper calls, inline script blocks, or production `.lua` files.
- Data/Text ID pairing for matching SunExp CSV files under `Data/` and `Text/`, including role-specific files such as `wuna.csv`.
- Data/Text ID pairing for Map when present. Tables with no Text side, such as current `Data/Level`, are allowed.
- EventList text shape for `TotalDescribe` and scripted option descriptions.
- Retired event ids such as `wuna_event_`, `Sub_wuna_event_`,
  `Sub_solar_finale_`, and `Sub_solar_memory_start`.
- `PackBelong` references for cards and relics.
- Mod resource paths that point to missing files.
- Enemy animation folders that need `Map/*.png` or `Map/*.jpg` frames for map icons.
- Supported `Text/Map.Note` values used by map UI.
- Basic `{0}` style placeholder consistency for card descriptions.

## Manual checks

Automated checks do not prove Unity runtime semantics. Manually reason through:

- Does every target effect set the intended status first?
- Does dynamic display match runtime behavior?
- Does every event listener avoid duplicated hooks?
- Does every changed behavior update player-facing text?
- Does any changed hook, audio provider, BGM provider, animated icon runtime, or game API wrapper need in-game verification?
- Does a compatibility wrapper support the current Managed signature, known legacy signature, and a deterministic fallback?
- Does each custom map node have `NodeDice`, and are shared run/map mutations host-authoritative?
- Can one failed lifecycle step prevent unrelated setup from running?
- Does visual runtime work need VisualBundle rebuild, shader/material checks, or
  in-game overlay/raycast verification?
- Does shared runtime work require packaged `Aura.Shared.dll` hash validation
  across all consumers?

## Known limitations

The local validation script cannot load the Unity runtime, instantiate `ScriptExecutor`, or prove UI/DLL hook behavior. Treat any UI hook, card-pack selection rule, or Managed-layer behavior as needing in-game verification.
