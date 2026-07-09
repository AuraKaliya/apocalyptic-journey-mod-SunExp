---
name: sunexp-shared-runtime-dev
description: Project-local skill for editing or reviewing SunExp and AuraToolsExp integration with Aura shared runtimes and cross-mod components, including the content-mod/tool-mod/shared-foundation boundary, AuraSharedCore, shared resources, AuraJourneyShared, AuraSkinShared, AuraAudioShared, BattleBgmArbiterShared, StarterDeckArbiterShared, AuraCgShared, AuraOnlineShared, AuraLogShared, UI safety runtimes, shared DLL packaging, shared release gates, owner ids, initialization registration, tool-local persistent overrides, sync scenario modeling, timing and duplicate suppression, compatibility protocols, RPC sender authority, and multiplayer authority.
---

# SunExp Shared Runtime Dev

Use this skill when SunExp touches Aura.Shared components, shared resource
manifests, packaged `Aura.Shared.dll`, or cross-mod runtime contracts.
Use it when aligning initialization registration, tool-local configuration
overrides, multi-mod sync, network timing, duplicate suppression, payload
guards, or chunked transfers.
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
   - Initialization registration, registered defaults, or effective tool
     configuration overrides.
   - Cross-mod RPC sender authority, payload guards, or chunked transports.
   - Multiplayer timing, replay/idempotency, sequence/version/hash semantics,
     duplicate suppression, or lifecycle cleanup.
2. Inspect the current integration:
   - `SunExp-Dev/SunExp.Dll.csproj`
   - `SunExp-Dev/Entry.cs`
   - affected `SunExp-Dev/GameApi/*` or `SunExp-Dev/Hooks/*`
   - `docs/shared-component-architecture-guidelines.md`
   - `docs/aura-shared-core-v2-contract.md`
   - `tools/shared-release-matrix.json`
   - `tools/Test-SharedDllPackaging.ps1`
   - `tools/Test-NetworkRpcAuthority.ps1`
3. Load `references/shared-boundaries.md` for Core/domain/adapter rules,
   content/tool ownership, shared presentation protocols, and multiplayer
   authority classification. Load
   `references/content-tool-shared-boundary.md` when deciding whether a
   reusable runtime belongs in SunExp, AuraToolsExp, or shared infrastructure,
   or when applying the content/tool configuration precedence model.
   Load
   `references/sunexp-shared-integration.md` for SunExp-specific integration
   points. Load `references/sync-scenario-model.md` when the task involves
   initialization registration, tool-local overrides, multi-mod sync, RPC
   authority, timing, duplicate suppression, or payload/chunk transfer
   semantics. Load
   `references/shared-dll-and-release-gates.md` when shared DLL packaging,
   release gates, or consumer compatibility are involved.
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
- Keep SunExp and AuraToolsExp as sibling consumers of shared foundations.
  Shared code must not depend on SunExp content semantics, and AuraToolsExp
  must not depend on SunExp internal runtime helpers.
- Initialization registration is not content-mod-exclusive. SunExp and
  AuraToolsExp may both register extension declarations they own; identity must
  still be `ownerModId` plus stable domain id.
- Preserve the content/tool split: content mods own, install, and register
  resources plus manifest semantics; tool mods only read shared registries,
  parse them by protocol, register tool-owned extensions, and manage local
  overrides.
- Keep registered defaults separate from tool-local effective configuration.
  Content-owned shared declarations default to enabled when used alone.
  AuraToolsExp local persistence wins for tool-managed effective behavior when
  a tool and content mod both configure the same shared feature, but must not
  rewrite or re-own a foreign mod's registration source.
- When both SunExp and AuraToolsExp need the same hook lifecycle, UI primitive,
  resource preload, logging, pooling, or multiplayer presentation behavior,
  promote the semantic-free part to a shared component instead of making
  SunExp the implicit base framework.
- Put cross-mod presentation protocols, such as Skill CG playback, in the
  shared domain component. Content mods declare resources and trigger requests;
  tool mods configure or override; neither owns private multiplayer relay or
  de-duplication for the shared feature.
- Use `references/sync-scenario-model.md` as the source of truth for
  synchronized event shape, RPC authority, payload fields, timing, and
  duplicate suppression.
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
