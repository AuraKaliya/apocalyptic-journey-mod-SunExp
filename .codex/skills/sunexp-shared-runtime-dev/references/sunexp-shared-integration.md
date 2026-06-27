# SunExp Shared Integration

Use this reference for SunExp-specific shared runtime entry points.

## Shared Runtime DLL

`SunExp-Dev/SunExp.Dll.csproj` references the real shared runtime assembly via
`AuraSharedRuntime-Dev/Aura.Shared.csproj`. Do not link shared source directly
into product Mod assemblies. Current shared runtime surfaces include:

- `AuraSharedCore`
- `AuraAudioShared`
- `AuraLogShared`
- `AuraJourneyShared`
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
- starter deck profiles;
- shared skin runtime and package;
- journey runtime;
- audio runtime;
- UI transition guard;
- gameplay hooks and special tags.

One failed shared step should be logged with its step name and should not hide
the identity of the failing subsystem.

## Resource Paths

Shared resources should resolve through the shared resource layer. Audio and BGM
providers may use `Shared:` paths; do not regress to bare local-path assumptions
when the manifest expects shared package installation.

## Solar Memory Touchpoints

Solar Memory uses shared Journey definitions and shared StarterDeck arbitration.
Route graph, map projection, and final role commit must respect owner-qualified
ids and authority-gated state transitions.
