# Validation Selection

Select evidence for the changed contract. The executable inventories are
[Terrias matrix](../../../../tools/terrias-test-matrix.json) and
[shared matrix](../../../../tools/shared-release-matrix.json). Inspect their
current steps with `tools/Get-AuraProjectContext.ps1` or the gate's `-List`.
The current `-List` output is the full enabled inventory; it does not apply
profile/tag filters or execute the selected checks.

## Impact selection

| Changed contract | Starting checks |
| --- | --- |
| Skill or routed developer documentation | tools/Test-ProjectSkills.ps1; add tests of any changed helper |
| Terrias Data/Text | tools/Test-TerriasContent.ps1; affected content validator |
| Terrias event/map rows | tools/Test-TerriasEvents.ps1; the events profile when native event-data semantics change |
| Terrias resource or registry | tools/Test-TerriasResources.ps1 |
| Terrias ordinary C# behavior | tools/Test-TerriasGate.ps1 -Profile csharp |
| Terrias feature behavior | affected domain profile; build the product once when shipping C# changes |
| Terrias layering, hooks or CSV entry | add tools/Test-TerriasArchitecture.ps1 |
| Shared internal behavior | focused shared-domain suite and tools/Build-AuraSharedRuntime.ps1 |
| Shared public API or compatibility | shared compatibility and consumer checks after reviewing the API delta |
| Shared RPC authority, dedupe or transport | shared network profile |
| AuraTools module/config/UI | tools/Test-AuraToolsExp.ps1; runtime preview for Unity UI changes |
| AI decision or simulation | focused AI behavior/knowledge/simulation checks from the combat-ai skill |
| Worker storage or recovery | tools/Test-AuraFoundationTrainerStorage.ps1 |
| Worker integration | foundation profile or explicitly selected worker checks |
| Product references or DLL publication | product build transaction and tools/Test-SharedDllPackaging.ps1 |
| Terrias release candidate | tools/Test-TerriasGate.ps1 -Profile full-release |
| Shared/product release candidate | tools/Test-SharedReleaseGate.ps1 -Profile full-release |
| Prototype maintenance | tools/Test-TestMods.ps1 only |

Profiles and tags are selectors, not dependency resolution. `-StepId` or
filtered `-Tag` can omit an earlier build that a selected step assumes.
Inspect the selected inventory and include its prerequisites explicitly.
When combining selections, execute each required suite/build once.

## Build and publish semantics

- `tools/Build-MainSharedConsumers.ps1` builds the canonical shared assembly,
  compiles the products in the consumer manifest, then publishes both packages.
- `tools/Build-TerriasDll.ps1` and `tools/Build-AuraToolsExpDll.ps1` are
  product-facing entries into that same transaction. Choose one, not both.
- `tools/Test-TerriasCSharp.ps1` builds products unless `-SkipBuild` is set.
  The csharp matrix already runs one build and passes that switch to tests.
- Direct `dotnet build` and `Build-AuraSharedRuntime.ps1` do not by themselves
  publish current product packages. Use the product transaction before claiming
  a shipped C# fix; focused internal checks may stop at compilation during work.
- Training executables use `tools/Build-AuraFoundationTrainer.ps1` explicitly.
  Ordinary tool UI or shared changes do not require rebuilding the trainer.
- `Publish-MainSharedConsumers.ps1` is the sole writer of product Entry/shared
  DLLs. The manifest commits last and failures roll back staged publication.
- Run commands sharing DLL outputs serially. Read-only discovery may run in
  parallel. Deploying repository packages into an actual game installation is
  a separate operation with installed-path/hash verification.

Example for ordinary Terrias C# work, without a preceding Build:

```powershell
tools/Test-TerriasGate.ps1 -Profile csharp
```

Example after an already completed product build:

```powershell
tools/Test-TerriasCSharp.ps1 -SkipBuild
```

## Test ownership and retirement

Keep one authoritative test for each invariant; other layers prove their
integration. Domain behavior belongs to the owning C# suite; content belongs
to the owning product's structured validator; public ABI belongs to shared
compatibility; package identity belongs to the packaging gate.

Retain tests for current behavior, supported formats, authority/ownership
boundaries, or release artifacts. Replace source snapshots when the contract
remains but implementation details changed. Retire completed migration and
private-layout assertions when they no longer protect a supported contract.
Do not automatically delete compatibility-reader tests for retained user data.

Expensive training, worker integration, simulation acceptance and archive
maintenance remain explicit selections. Product/shared validation excludes
TestMods. PowerShell source scans may enforce generic boundaries, not feature
algorithms or private method order.

## Runtime evidence

Compilation, public-member reflection and .NET tests do not establish Unity
rendering, native lifecycle order or multiplayer semantics.

For pooled/shared mutations cover nested owners, release interleavings,
duplicate release, partial failure, external mutation, destroyed targets and
generation reuse. For synthetic native objects cover route registration,
local-self and remote-target effects, exact executor restoration and failed
initialization cleanup.

Select the real-game cases from the owning feature, including next reuse or
next battle after teardown. Replay rendering uses its
[runtime matrix](../../aura-battle-replay-dev/references/validation-matrix.md).
Report automated evidence, runtime evidence and unexecuted acceptance
separately. A large assertion count or clean top-level log is insufficient.

## Validated release receipt

Use tools/Invoke-AuraValidatedRelease.ps1 for the combined remediation release.
It binds current input hashes, both product assemblies and Aura.Shared to test
receipts before the single publisher runs. Full package deployment and interrupted
installation recovery use Deploy-AuraProducts.ps1 and Restore-AuraProductDeployment.ps1.
A direct development build remains valid for compilation, but is not a validated
installation. Source changes invalidate the existing release input snapshot.
