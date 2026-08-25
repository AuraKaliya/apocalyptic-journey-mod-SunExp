---
name: terrias-shared-runtime-dev
description: Project-local skill for editing or reviewing Terrias and AuraToolsExp integration with Aura shared runtimes and cross-mod components, including the content-mod/tool-mod/shared-foundation boundary, AuraSharedCore, shared resources, shared mutable Unity/runtime ownership, pooled presentation generations, AuraJourneyShared, AuraSkinShared, AuraAudioShared, BattleBgmArbiterShared, StarterDeckArbiterShared, AuraCgShared, AuraOnlineShared, AuraLogShared, UI safety runtimes, shared DLL packaging, shared release gates, owner ids, initialization registration, tool-local persistent overrides, sync scenario modeling, timing and duplicate suppression, compatibility protocols, RPC sender authority, and multiplayer authority.
---

# Terrias Shared Runtime Dev

Use this skill when Terrias touches Aura.Shared components, shared resource
manifests, packaged `Aura.Shared.dll`, or cross-mod runtime contracts.
Use it when aligning initialization registration, tool-local configuration
overrides, multi-mod sync, network timing, duplicate suppression, payload
guards, or chunked transfers.
Pair it with `terrias-mod-dev`; pair it with `terrias-solar-memory-dev` for
Journey, starter deck, or Solar Memory role setup work.
Pair it with `terrias-visual-runtime-dev` for Skill CG, CG overlays, or shared
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
   - Shared mutable Unity/runtime targets, temporary ownership stacks, pooled
     view generations, or cross-consumer cleanup obligations.
   - Shared DLL packaging or consumer project references.
   - Initialization registration, registered defaults, or effective tool
     configuration overrides.
   - Cross-mod RPC sender authority, payload guards, or chunked transports.
   - Multiplayer timing, replay/idempotency, sequence/version/hash semantics,
     duplicate suppression, or lifecycle cleanup.
2. Inspect the current integration:
   - `Terrias-Dev/Terrias.Dll.csproj`
   - `Terrias-Dev/Entry.cs`
   - affected `Terrias-Dev/GameApi/*` or `Terrias-Dev/Hooks/*`
   - `docs/aura-shared-core-v2-contract.md`
   - `docs/Terrias/04-Aura共享层与核心层接入.md`
   - `tools/shared-release-matrix.json`
   - `tools/Test-SharedDllPackaging.ps1`
   - `tools/Test-NetworkRpcAuthority.ps1`
3. Load `references/shared-boundaries.md` for Core/domain/adapter rules,
   content/tool ownership, shared presentation protocols, and multiplayer
   authority classification. Load
   `references/shared-mutable-runtime-ownership.md` when sibling consumers
   mutate the same Renderer, material, Sprite, pooled view, overlay, or other
   temporary runtime property. Load
   `references/content-tool-shared-boundary.md` when deciding whether a
   reusable runtime belongs in Terrias, AuraToolsExp, or shared infrastructure,
   or when applying the content/tool configuration precedence model.
   Load
   `references/terrias-shared-integration.md` for Terrias-specific integration
   points. Load `references/sync-scenario-model.md` when the task involves
   initialization registration, tool-local overrides, multi-mod sync, RPC
   authority, timing, duplicate suppression, or payload/chunk transfer
   semantics. Load
   `references/shared-dll-and-release-gates.md` when shared DLL packaging,
   release gates, or consumer compatibility are involved.
4. Add or adjust shared release checks when a new cross-mod boundary must stay
   stable.
5. Select validation from the impact matrix in
   `../terrias-mod-dev/references/validation-rules.md`. Do not treat every
   shared edit as a reason to run every consumer, network, packaging, and
   release gate.

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
  release checks, not only Terrias.
- Keep Terrias and AuraToolsExp as sibling consumers of shared foundations.
  Shared code must not depend on Terrias content semantics, and AuraToolsExp
  must not depend on Terrias internal runtime helpers.
- Content MODs do not actively register optional media from their `Entry`.
  They may carry owner-qualified declarations under `SharedResources`; the
  AuraToolsExp discovery adapter scans only actually loaded MODs, deduplicates
  physical sources by numeric `.modproj` id, and registers them through shared
  domain protocols. Semantic identity remains `ownerModId` plus domain id.
- Preserve the project product boundary: Terrias owns content mechanics,
  required presentation, and Terrias-carried optional voice/CG files and
  declarations. AuraToolsExp owns discovery, local effective configuration,
  editors, replacement skins, card themes/effects, and tool defaults. Shared
  runtimes own storage, registries, playback, networking, and reconciliation.
- Keep registered defaults separate from tool-local effective configuration.
  Content-owned shared declarations default to enabled when used alone.
  AuraToolsExp local persistence wins for tool-managed effective behavior when
  a tool and content mod both configure the same shared feature, but must not
  rewrite or re-own a foreign mod's registration source.
- AuraToolsExp custom-start card/relic loadouts are local or imported tool
  configuration. Content mods must not register AuraTools custom-start
  profiles; keep `StarterDeckArbiterShared` only for generic application and
  ownership contracts used by content-owned modes and compatibility.
- When both Terrias and AuraToolsExp need the same hook lifecycle, UI primitive,
  resource preload, logging, pooling, or multiplayer presentation behavior,
  promote the semantic-free part to a shared component instead of making
  Terrias the implicit base framework.
- When sibling consumers temporarily mutate the same mutable target, one shared
  coordinator owns the baseline, logical generation, release ordering, and
  dirty-state decision. Consumers must not capture and restore independent
  "original" values for that target.
- A deferred or skipped cleanup must leave a durable pending obligation with a
  named owner and deterministic drain trigger. A warning followed by return is
  not a valid handoff.
- Put cross-mod presentation protocols, such as Skill CG playback and card UI
  lifecycle, in the shared domain component. Content MODs may declare their
  own optional media; AuraToolsExp discovers/configures it and observes stable
  content signals. Neither consumer owns a private multiplayer relay or
  de-duplication path.
- Use `references/sync-scenario-model.md` as the source of truth for
  synchronized event shape, RPC authority, payload fields, timing, and
  duplicate suppression.
- Keep all packaged `Aura.Shared.dll` copies hash-identical after shared runtime
  builds.
- Read product/test classification from `tools/shared-consumers.json`. Product
  projects must not copy Entry or Aura.Shared from MSBuild targets; only the
  staged `Publish-MainSharedConsumers.ps1` transaction writes product packages.
- Keep archived prototypes under `TestMods` outside product and shared release
  validation. Run `tools/Test-TestMods.ps1` only when a task explicitly targets
  those prototypes.

## Validation

Choose checks by affected contract:

```powershell
tools\Test-AuraSkinShared.ps1 # selection, path, package preflight, and protocol behavior
tools\Test-AuraCgShared.ps1 # AuraCgShared behavior
tools\Test-AuraSharedCore.ps1 # Core storage/protocol behavior
tools\Build-AuraSharedRuntime.ps1 # production Aura.Shared build
tools\Test-SharedRuntimeCompatibility.ps1 # public shared API/protocol shape
tools\Build-MainSharedConsumers.ps1 # public shared API changed
tools\Test-SharedReleaseGate.ps1 -Profile network # RPC behavior or authority changed
tools\Test-NetworkRpcAuthority.ps1 # generic command registration/transport scan
tools\Test-SharedDllPackaging.ps1 # project references or packaged DLL changed
tools\Test-SharedReleaseGate.ps1 -Profile skin # focused shared domain
tools\Test-SharedReleaseGate.ps1 -Tag public-api # impact-tag selection
tools\Test-SharedReleaseGate.ps1 -Profile full-release # release candidate
```

Run commands serially when they write shared DLL outputs. Internal domain
changes need the focused domain suite and shared build; add consumer builds
only when a public surface changes. The full release gate is a release-level
check, not the default response to every shared source edit.
The `network` profile runs Core, CG, and Audio network behavior before the
generic RPC registration/transport scan. Do not replace those behavior suites
with source-token assertions.
Keep compatibility baselines limited to reflected public API data. Private
class names and implementation snippets belong neither in compatibility nor
architecture gates.
Use `tools\Test-SharedReleaseGate.ps1 -List` to inspect the current owner,
category, cost, profile, and impact-tag inventory before selecting a gate.
