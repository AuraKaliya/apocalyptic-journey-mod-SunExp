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
- `SanGuoShaExp-Dev/SanGuoShaExp.Dll.csproj`
- test/prototype consumers listed in `tools/Test-SharedDllPackaging.ps1`

After shared runtime changes, all packaged `Aura.Shared.dll` copies should have
the same hash as the built shared runtime DLL.

## Release Matrix

`tools/shared-release-matrix.json` is the shared gate inventory. Keep it aligned
with real shared contracts and run `tools/Test-SharedReleaseGate.ps1` when
shared surfaces change.

Current gate families include:

- core contract and raw shared write scans;
- architecture guideline checks;
- network RPC authority checks;
- AuraTools feature tests;
- main shared consumer tests;
- shared DLL packaging validation.

## RPC Authority Gate

`tools/Test-NetworkRpcAuthority.ps1` guards the cross-mod authority model:

- server-bound commands receive sender context from receive hooks;
- Terrias Solar Memory role commit validates sender and role identity;
- AuraTools DamageMeter control/snapshot/report paths do not trust payload
  issuer or reporter fields;
- oversized payloads are blocked or chunked before network serialization.

## Validation

Use the broad gate for shared protocol or packaging changes:

```powershell
tools\Build-TerriasDll.ps1
tools\Build-AuraToolsExpDll.ps1
tools\Test-NetworkRpcAuthority.ps1
tools\Test-SharedArchitectureGuidelines.ps1
tools\Test-AuraSharedCore.ps1
tools\Test-SharedReleaseGate.ps1
tools\Test-SharedDllPackaging.ps1
```
