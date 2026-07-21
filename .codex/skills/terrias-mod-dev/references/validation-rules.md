# Validation Rules

Use this reference before finishing any Terrias content change.

## Required automated checks

Run the base chain serially:

```powershell
tools\Build-TerriasDll.ps1
tools\Test-TerriasArchitecture.ps1
tools\Test-TerriasCSharp.ps1
.codex\skills\terrias-mod-dev\scripts\validate-terrias.ps1
```

Run these serially. `Build-TerriasDll.ps1` and `Test-TerriasCSharp.ps1` can both
write `Terrias-Dev/obj/Release/net472/Terrias.Aura.dll` and should not be
parallelized.

Add scenario checks as needed:

```powershell
.codex\skills\terrias-event-dev\scripts\validate-terrias-events.ps1 # EventList or Map
tools\Build-TerriasVisualBundle.ps1 # VisualAssets, shaders, bundled CG, or VisualBundles
tools\Test-NetworkRpcAuthority.ps1 # Terrias or AuraTools server-bound RPC authority
tools\Test-SharedArchitectureGuidelines.ps1 # shared runtime contract or docs
tools\Test-AuraSharedCore.ps1 # AuraSharedCore or shared protocol
tools\Test-SharedReleaseGate.ps1 # broad shared release compatibility
tools\Test-SharedDllPackaging.ps1 # packaged Aura.Shared.dll references or hashes
tools\Build-AuraToolsExpDll.ps1 # AuraTools shared consumer or Skill CG tool changes
.codex\skills\terrias-skill-evolution\scripts\audit-terrias-skill-staleness.ps1 # skill or architecture-boundary updates
```

This checks:

- C# compile, architecture assertions, and focused Terrias C# regression tests.
- Old dynamic helper calls, inline script blocks, or production `.lua` files.
- Data/Text ID pairing for matching Terrias CSV files under `Data/` and `Text/`, including role-specific files such as `wuna.csv`.
- Data/Text ID pairing for Map when present. Tables with no Text side, such as current `Data/Level`, are allowed.
- EventList text shape for `TotalDescribe` and scripted option descriptions.
- Removed historical event ids guarded by the validation script.
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
