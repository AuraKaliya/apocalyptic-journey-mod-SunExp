# Validation Rules

Select validation from the changed contract and its blast radius. Do not run a
fixed repository-wide chain for every Terrias or shared edit.

## Impact Matrix

| Change | Required validation |
| --- | --- |
| Terrias Data/Text only | `validate-terrias.ps1` plus the affected domain validator |
| Terrias resources or registries | `tools/Test-TerriasResources.ps1` |
| Terrias C# behavior | `tools/Build-TerriasDll.ps1` product transaction plus the focused C# or domain tests |
| Terrias architecture, hooks, or CSV entry boundaries | add `tools/Test-TerriasArchitecture.ps1` |
| Synthetic Partner/status object or ScriptExecutor locality | Terrias C# behavior tests plus Architecture; add the shared `network` profile only when the custom RPC/authority contract changes |
| One Terrias feature domain | `tools/Test-TerriasGate.ps1 -Profile <domain>` |
| One shared domain's internal behavior | `tools/Build-AuraSharedRuntime.ps1` plus that domain's behavior suite |
| Shared public API, schema, or compatibility range | add `tools/Test-SharedRuntimeCompatibility.ps1` and affected main-consumer builds |
| Shared Core storage or resource protocol | `tools/Test-AuraSharedCore.ps1` and the focused domain suite |
| Shared mutable presentation target or pooled generation contract | Core coordinator state-machine tests plus focused integration in every real consumer; add main-consumer builds, compatibility, and packaging only when the public shared surface changes |
| Shared RPC sender authority, payload, dedupe, or lifecycle | `tools/Test-SharedReleaseGate.ps1 -Profile network` |
| RPC command registration or transport boundary scanner | `tools/Test-NetworkRpcAuthority.ps1` |
| Combat AI shared behavior | `tools/Test-SharedReleaseGate.ps1 -Profile combat-ai` |
| Foundation worker implementation | `tools/Test-SharedReleaseGate.ps1 -Profile foundation` |
| Consumer project references or packaged shared DLLs | `tools/Test-SharedDllPackaging.ps1` after affected builds |
| Broad shared cross-domain change or release candidate | `tools/Test-SharedReleaseGate.ps1 -Profile full-release` |
| Terrias release candidate | `tools/Test-TerriasGate.ps1 -Profile full-release` |
| Skill-only change | skill quick validation and Terrias skill staleness audit |
| TestMods prototype maintenance | `tools/Test-TestMods.ps1` only |

Commands that write `Terrias.Aura.dll`, `Entry.dll`, or `Aura.Shared.dll` must
run serially when they share an output path.

## Behavior Matrices

For lifecycle and native-integration defects, a large assertion count is not a
substitute for covering the branch that failed.

Shared mutable/pool tests should cover nested ownership, out-of-order release,
duplicate release, partial write and rollback failure, external mutation,
destroyed targets, cross-generation reuse, the post-reset clean gate, and at
least two real consumers of the same target.

Synthetic native-object tests should cover owner registration and migration,
failed-init cleanup, local self effects, an enemy or other non-local target that
still takes the native RPC branch, exact ScriptExecutor context restoration on
success and exception, and preservation rather than invention of native routing
Vars.

Test an interleaving or target/locality matrix when the runtime symptom depends
on ordering. Do not reduce it to isolated one-method happy paths.

## Ownership

- Shared behavior belongs to a focused shared-domain test project.
- Core storage, path, transaction, and registry behavior belongs to Core tests.
- Terrias content ids, assets, and presentation declarations belong to Terrias
  validators.
- AuraToolsExp-owned content and effective tool configuration belong to the
  AuraToolsExp suite.
- Cross-mod packaging and ownership boundaries have one authoritative shared
  gate; consumer suites should keep only a focused integration smoke test.
- `TestMods` contains archived prototypes. It is never a release consumer and
  must not be pulled into shared/core, AuraToolsExp, or Terrias validation.

## Test Retirement

A test may remain only when it maps to at least one current contract:

- observable behavior;
- public schema or compatibility promise;
- security, path, multiplayer authority, or ownership boundary;
- build/package/release artifact invariant;
- current product content owned by the suite.

Replace a test when the contract is current but the assertion only scans source
tokens, private method names, exact implementation structure, or file layout.
Delete a test when it only preserves a completed migration, retired id, old
protocol number, removed file, duplicate invariant, or one-time implementation
snapshot. Migration tests must declare an exit condition and must not become
permanent negative source scans.

Keep one authoritative test for each invariant. Other layers may prove that
they integrate with the contract, but must not copy its full assertion set.

## Manual Checks

Automated checks do not prove Unity runtime semantics. Manually reason through
changed hooks, UI layout/raycast behavior, animation fallback, multiplayer
timing, and Managed signature compatibility. Run in-game verification when the
change depends on Unity objects or host lifecycle order.

Use a fresh runtime log for manual acceptance. A top-level completed action or
clean exception count does not prove each effect branch: require observable
evidence for the local self path, the non-local/network target path, generation
cleanup, and the next reuse after the failure-prone interleaving.

## Focused Commands

```powershell
tools\Build-TerriasDll.ps1
tools\Test-TerriasCSharp.ps1
pwsh -NoProfile -File tools\Test-TerriasArchitecture.ps1
tools\Test-TerriasResources.ps1
tools\Test-TerriasGate.ps1 -List
tools\Test-TerriasGate.ps1 -Profile elemental
tools\Test-TerriasGate.ps1 -Profile spirit
.codex\skills\terrias-mod-dev\scripts\validate-terrias.ps1
.codex\skills\terrias-event-dev\scripts\validate-terrias-events.ps1
tools\Test-AuraSharedCore.ps1
tools\Build-AuraSharedRuntime.ps1
tools\Test-AuraSkinShared.ps1
tools\Test-AuraCgShared.ps1
tools\Test-AuraCombatAi.ps1
tools\Test-AuraCombatTrainingArtifacts.ps1
tools\Test-AuraCombatSimulationAcceptance.ps1
tools\Test-SharedRuntimeCompatibility.ps1
tools\Build-MainSharedConsumers.ps1
tools\Test-NetworkRpcAuthority.ps1
tools\Test-SharedDllPackaging.ps1
tools\Test-SharedReleaseGate.ps1 -Profile network
tools\Test-SharedReleaseGate.ps1 -Profile combat-ai
tools\Test-SharedReleaseGate.ps1 -Profile full-release
```

`tools/terrias-test-matrix.json` is the authoritative Terrias validation
inventory. Every enabled step declares its owner, category, cost, impact tags,
and profiles. Select by `-Profile`, `-Tag`, or `-StepId`; do not add hidden
child-suite calls to feature or architecture scripts.

The `spirit` profile explicitly selects three independent contracts: structured
content, registry schema behavior, and runtime behavior. Keep those entries
separate rather than rebuilding a hidden Spirit aggregate inside any script.
