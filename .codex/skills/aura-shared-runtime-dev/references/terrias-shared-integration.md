# Terrias Shared Integration

Use this reference for Terrias-specific shared runtime entry points.

## Shared Runtime DLL

`Terrias-Dev/Terrias.Dll.csproj` references the real shared runtime assembly via
`AuraSharedRuntime-Dev/Aura.Shared.csproj`. Do not link shared source directly
into product Mod assemblies. Current shared runtime surfaces include:

- `AuraSharedCore`
- `AuraAudioShared`
- `AuraCgShared`
- `AuraLogShared`
- `AuraJourneyShared`
- `AuraOnlineShared`
- `AuraSkinShared`
- `AudioArbiterShared`
- `BattleBgmArbiterShared`
- `StarterDeckArbiterShared`
- `UiRaycastSafetyShared`
- `UiTransitionGuardShared`

Changes to these shared roots affect their supported consumers. Read product
classification from tools/shared-consumers.json; publish and verify the
canonical shared DLL and product package copies together. Archived TestMods
copies are outside this transaction and are checked only for explicit prototype
maintenance. Select consumer checks through the
[impact guide](../../aura-project-dev/references/validation.md).

## Entry Initialization

`Terrias-Dev/Entry.cs` initializes runtime-owned shared systems in separately named `RunStep`
calls. Preserve step isolation for:

- XLua assembly registration;
- shared core; optional media package registration is owned by AuraToolsExp discovery;
- shared registry;
- Terrias-required visual registry;
- shared starter-deck ownership/application for Terrias-owned modes;
- journey runtime;
- UI transition guard;
- gameplay hooks and special tags.

One failed shared step should be logged with its step name and should not hide
the identity of the failing subsystem.

`Terrias-Dev/Entry.cs` should initialize `TerriasRpcAuthorityRuntime` before
server-bound Terrias RPC commands can be applied.

## Resource Paths

Shared resources should resolve through the shared resource layer. Audio and BGM
providers may use `Shared:` paths; do not regress to bare local-path assumptions
when the manifest expects shared package installation.

Terrias carries its optional voice/CG assets and manifests under
`SharedResources`, with `aura.discovery.json` as the sole discovery entry.
AuraToolsExp scans only loaded MODs, binds physical source identity to
`Terrias.modproj`, registers the declarations through shared protocols, and
applies local effective configuration. Terrias keeps no active media install or
playback step and no empty retirement manifest.

## Solar Memory Touchpoints

Solar Memory uses shared Journey definitions and shared StarterDeck arbitration.
Route graph, map projection, and final role commit must respect owner-qualified
ids and authority-gated state transitions.

Final role commit also uses Terrias RPC sender binding. Remote commits must
validate the bound sender against the submitted `Role.Id`; host-local direct
commits should use the same local server sender model.
