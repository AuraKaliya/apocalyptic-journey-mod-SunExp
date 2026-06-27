---
name: sunexp-shared-runtime-dev
description: Project-local skill for editing or reviewing SunExp integration with Aura shared runtimes and cross-mod components, including AuraSharedCore, shared resources, AuraJourneyShared, AuraSkinShared, AuraAudioShared, BattleBgmArbiterShared, StarterDeckArbiterShared, UI safety runtimes, shared release gates, owner ids, compatibility protocols, and multiplayer authority.
---

# SunExp Shared Runtime Dev

Use this skill when SunExp touches shared components or linked shared source.
Pair it with `sunexp-mod-dev`; pair it with `sunexp-solar-memory-dev` for
Journey, starter deck, or Solar Memory role setup work.

## Workflow

1. Identify the shared surface:
   - `AuraSharedCore` storage, registry, package install, paths, or hooks.
   - `AuraJourneyShared` route/state/map projection.
   - `AuraSkinShared` shared skin package or registry.
   - `AuraAudioShared`, `AudioArbiterShared`, or `BattleBgmArbiterShared`.
   - `StarterDeckArbiterShared`.
   - `UiRaycastSafetyShared` or `UiTransitionGuardShared`.
2. Inspect the current integration:
   - `SunExp-Dev/SunExp.Dll.csproj`
   - `SunExp-Dev/Entry.cs`
   - affected `SunExp-Dev/GameApi/*` or `SunExp-Dev/Hooks/*`
   - `docs/shared-component-architecture-guidelines.md`
   - `docs/aura-shared-core-v2-contract.md`
   - `tools/shared-release-matrix.json`
3. Load `references/shared-boundaries.md` for Core/domain/adapter rules. Load
   `references/sunexp-shared-integration.md` for SunExp-specific integration
   points.
4. Add or adjust shared release checks when a new cross-mod boundary must stay
   stable.

## Hard Rules

- Keep Core semantic-free. Domain meaning belongs in domain shared components.
- Give every registered artifact a stable owner identity.
- Do not bypass `AuraSharedCore` for shared config, registry, or package writes.
- Shared state changes must be authority-gated in multiplayer.
- Compatibility checks must reject incompatible global components without
  crashing unrelated gameplay initialization.
- Use `Entry.RunStep` or equivalent step isolation for shared initialization.
- Do not move domain resolution policy into adapters. Adapters install, bridge,
  and delegate; domain arbiters validate and resolve.
- Keep linked shared source compatible with all consumers listed in shared
  release checks, not only SunExp.
- Preserve the content/tool split: content mods own, install, and register
  resources plus manifest semantics; tool mods only read shared registries,
  parse them by protocol, and manage local overrides.

## Validation

Run affected consumer builds and shared gates serially:

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
tools\Test-SharedArchitectureGuidelines.ps1
tools\Test-AuraSharedCore.ps1
tools\Test-SharedReleaseGate.ps1
```

When a shared source is consumed by multiple mods, also run the relevant
consumer build script, such as `tools\Build-MainSharedConsumers.ps1` or
`tools\Build-SharedRuntimeConsumers.ps1`.
