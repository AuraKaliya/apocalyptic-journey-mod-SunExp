# SunExp Shared Integration

Use this reference for SunExp-specific shared runtime entry points.

## Shared Runtime DLL

`SunExp-Dev/SunExp.Dll.csproj` references the real shared runtime assembly via
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

Changes to these shared roots affect every Mod that packages `Aura.Shared.dll`.
Check the shared release matrix, rebuild affected consumers, and verify all
packaged `Aura.Shared.dll` copies have the same hash before finishing.

## Entry Initialization

`SunExp-Dev/Entry.cs` initializes shared systems in separately named `RunStep`
calls. Preserve step isolation for:

- XLua assembly registration;
- shared core and shared resource package;
- shared registry;
- visual registry and CG registry;
- starter deck profiles;
- shared skin runtime and package;
- journey runtime;
- audio runtime;
- UI transition guard;
- gameplay hooks and special tags.

One failed shared step should be logged with its step name and should not hide
the identity of the failing subsystem.

`SunExp-Dev/Entry.cs` should initialize `SunExpRpcAuthorityRuntime` before
server-bound SunExp RPC commands can be applied.

## Resource Paths

Shared resources should resolve through the shared resource layer. Audio and BGM
providers may use `Shared:` paths; do not regress to bare local-path assumptions
when the manifest expects shared package installation.

Skill CG resources should be installed through `SunExp/SharedResources/package.json`
and declared through `SunExp/SharedResources/cg.registry.json`. Tool mods should
consume those declarations through the shared CG protocol instead of scanning
SunExp private folders.

## Solar Memory Touchpoints

Solar Memory uses shared Journey definitions and shared StarterDeck arbitration.
Route graph, map projection, and final role commit must respect owner-qualified
ids and authority-gated state transitions.

Final role commit also uses SunExp RPC sender binding. Remote commits must
validate the bound sender against the submitted `Role.Id`; host-local direct
commits should use the same local server sender model.
