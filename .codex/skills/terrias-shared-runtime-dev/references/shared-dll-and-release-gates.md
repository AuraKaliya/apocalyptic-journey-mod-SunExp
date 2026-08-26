# Shared DLL And Release Gates

Use this reference when editing Aura.Shared runtime source, consumer project
references, packaged DLLs, or shared release scripts.

## DLL Packaging Model

Product Mod projects should reference `AuraSharedRuntime-Dev/Aura.Shared.csproj`
and package the built `Aura.Shared.dll`. Do not compile private copies of shared
source into each Mod assembly.

Important consumers include:

- `Terrias-Dev/Terrias.Dll.csproj`
- `AuraToolsExp-Dev/AuraToolsExp.Dll.csproj`

`tools/shared-consumers.json` is the single consumer inventory. SanGuoShaExp is
an explicit-only archived consumer under `TestMods`; it is not a product or
shared release consumer.

Product projects compile against the canonical shared build but do not write
package directories from MSBuild targets. After validation,
`Publish-MainSharedConsumers.ps1` stages Entry and Aura.Shared for both product
packages plus the release manifest, verifies source/stage hashes, commits the
manifest last as the transaction marker, rolls back in reverse commit order on
failure, and verifies final hashes. All product copies must match the canonical
DLL. Repository-relative manifest paths come from the PowerShell 5.1/7-compatible
`RepositoryPath.psm1` boundary rather than runtime-specific path APIs.

## Public API Cutover

Treat a compatibility-baseline change as evidence of a reviewed public cutover,
not as a way to repair a red gate.

1. Inspect the reflected API diff and state which additions, changes, or
   removals are intentional.
2. Migrate every supported product consumer and remove the superseded runtime
   path before accepting the new baseline.
3. Build the main consumers against the new shared assembly.
4. Rebuild packaged artifacts, run the packaging gate, and verify that every
   shipped `Aura.Shared.dll` hash matches the built shared runtime.
5. Use a focused residual search as one-time migration evidence when useful,
   but do not preserve a permanent private-symbol/source-token scan after the
   public contract and consumers already enforce the final state.

Do not recapture a baseline before understanding an unexpected diff. A green
baseline with an unbuilt consumer is not compatibility evidence.

## Release Matrix

`tools/shared-release-matrix.json` is the schema-v2 shared gate inventory. Each
enabled step must declare a unique id, owner, category, cost, impact tags, and
profiles. Keep it aligned with real shared contracts and select the narrowest
profile, tag, or explicit step that covers the changed surface.

Current gate families include:

- core contract and raw shared write scans;
- architecture guideline checks;
- network behavior for Core, CG, and Audio plus a generic RPC boundary scan;
- Combat AI behavior, knowledge, training artifacts, and simulation acceptance;
- Foundation worker integration and release-only archive maintenance;
- AuraTools feature tests;
- main shared consumer tests;
- shared DLL packaging validation.

## RPC Authority Gate

The `network` profile guards the cross-mod authority model in two layers:

- Core, CG, and Audio behavior suites prove sender scoping/authority, payload
  guards, bounded duplicate suppression, and lifecycle cleanup;
- `tools/Test-NetworkRpcAuthority.ps1` rejects payload identity authorization,
  raw transport outside approved adapters, server `CmdExecute` entries without
  a server-bound marker, and markers not registered through
  `AuraRpcAuthorityRuntime`.

## Validation

Choose the narrowest gate that proves the affected contract:

```powershell
tools\Test-AuraSharedCore.ps1 # Core storage or resource protocol
tools\Build-AuraSharedRuntime.ps1 # production shared assembly
tools\Test-SharedRuntimeCompatibility.ps1 # public shared API
tools\Build-MainSharedConsumers.ps1 # public surface consumed by product MODs
tools\Test-SharedReleaseGate.ps1 -Profile network # RPC behavior or authority changes
tools\Test-NetworkRpcAuthority.ps1 # generic RPC boundary scanner changes
tools\Test-SharedDllPackaging.ps1 # project references or DLL distribution
tools\Test-MainSharedConsumerPublishTransaction.ps1 # staged commit/rollback and PowerShell 5.1 compatibility
tools\Test-SharedReleaseGate.ps1 -List # inspect profiles and impact tags
tools\Test-SharedReleaseGate.ps1 -Profile domain # all shared domain behavior
tools\Test-SharedReleaseGate.ps1 -Tag public-api # tag-selected validation
tools\Test-SharedReleaseGate.ps1 -Profile full-release # comprehensive release validation
```

The runner executes selected steps serially because multiple builds may write
the same shared DLL outputs. Do not restore hidden child calls inside matrix
steps; an invariant or suite should appear once in the explicit matrix.
`Test-AuraSharedCore.ps1` proves behavior; `Build-AuraSharedRuntime.ps1` proves
the production assembly. The compatibility baseline records reflected public
API only and must not contain source snippets.

Archived `TestMods` projects are excluded from product consumer builds and the
shared release matrix. Their isolated entry is `tools/Test-TestMods.ps1`.
