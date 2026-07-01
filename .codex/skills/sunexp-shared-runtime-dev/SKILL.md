---
name: sunexp-shared-runtime-dev
description: Project-local skill for editing or reviewing SunExp integration with Aura shared runtimes and cross-mod components, including AuraSharedCore, shared resources, AuraJourneyShared, AuraSkinShared, AuraAudioShared, BattleBgmArbiterShared, StarterDeckArbiterShared, AuraCgShared, AuraOnlineShared, AuraLogShared, UI safety runtimes, shared DLL packaging, shared release gates, owner ids, compatibility protocols, RPC sender authority, and multiplayer authority.
---

# SunExp Shared Runtime Dev

Use this skill when SunExp touches Aura.Shared components, shared resource
manifests, packaged `Aura.Shared.dll`, or cross-mod runtime contracts.
Pair it with `sunexp-mod-dev`; pair it with `sunexp-solar-memory-dev` for
Journey, starter deck, or Solar Memory role setup work.
Pair it with `sunexp-visual-runtime-dev` for Skill CG, CG overlays, or shared
visual resources.

## Workflow

1. Identify the shared surface:
   - `AuraSharedCore` storage, registry, package install, paths, or hooks.
   - `AuraJourneyShared` route/state/map projection.
   - `AuraSkinShared` shared skin package or registry.
   - `AuraAudioShared`, `AudioArbiterShared`, or `BattleBgmArbiterShared`.
   - `AuraCgShared` CG registry, activation, overlays, or playback.
   - `AuraOnlineShared` chat, mod-sync snapshots, or online shared state.
   - `AuraLogShared` logging surfaces.
   - `StarterDeckArbiterShared`.
   - `UiRaycastSafetyShared` or `UiTransitionGuardShared`.
   - Shared DLL packaging or consumer project references.
   - Cross-mod RPC sender authority, payload guards, or chunked transports.
2. Inspect the current integration:
   - `SunExp-Dev/SunExp.Dll.csproj`
   - `SunExp-Dev/Entry.cs`
   - affected `SunExp-Dev/GameApi/*` or `SunExp-Dev/Hooks/*`
   - `docs/shared-component-architecture-guidelines.md`
   - `docs/aura-shared-core-v2-contract.md`
   - `tools/shared-release-matrix.json`
   - `tools/Test-SharedDllPackaging.ps1`
   - `tools/Test-NetworkRpcAuthority.ps1`
3. Load `references/shared-boundaries.md` for Core/domain/adapter rules. Load
   `references/sunexp-shared-integration.md` for SunExp-specific integration
   points. Load `references/shared-dll-and-release-gates.md` when shared DLL
   packaging, release gates, or consumer compatibility are involved.
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
- Keep the shared runtime DLL compatible with all consumers listed in shared
  release checks, not only SunExp.
- Preserve the content/tool split: content mods own, install, and register
  resources plus manifest semantics; tool mods only read shared registries,
  parse them by protocol, and manage local overrides.
- Do not authorize cross-mod server-bound RPC from payload-provided identity.
  Bind sender context at the server receive boundary and validate it centrally.
- Keep all packaged `Aura.Shared.dll` copies hash-identical after shared runtime
  builds.

## Validation

Run affected consumer builds and shared gates serially:

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
tools\Test-NetworkRpcAuthority.ps1
tools\Test-SharedArchitectureGuidelines.ps1
tools\Test-AuraSharedCore.ps1
tools\Test-SharedReleaseGate.ps1
tools\Test-SharedDllPackaging.ps1
```

When a shared source is consumed by multiple mods, also run the relevant
consumer build script, such as `tools\Build-MainSharedConsumers.ps1` or
`tools\Build-SharedRuntimeConsumers.ps1`.
