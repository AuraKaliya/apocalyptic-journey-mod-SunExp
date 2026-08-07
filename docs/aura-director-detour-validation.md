# Aura Director Detour Validation

## Decision

The Harmony route is approved for the Terrias local director on the verified
game build. It remains an optional provider outside `Aura.Shared.dll`; no other
MOD receives or auto-enables the dependency.

## Scope

The provider patches only the public, argument-free
`FightManager.ReadyToStart()` method. It does not read or mutate `readyCount`,
`fightType`, `ActionQueue`, `DOAllAction`, or any private progression surface.

The backend lives in `AuraDirectorDetour-Dev` and pins Lib.Harmony 2.4.2.
`Aura.Shared.dll` contains the backend-independent provider, hold, sink, request
source, session, compiler, and presentation contracts without referencing
Harmony or the optional backend assembly.

## Verified Builds

- target assembly: `Managed/Witch.dll`;
- game reference `v1.0.23816797`: `8D87696341625B19F63059B6D91262FF5738F3C0B5ABB7598A05C7640727790A`;
- game reference `v1.0.24591395`: `88613CF3E1F0F4A493FE722FBFB63E36A6C97CBF098F9F406F6AC2A28C136F60`;
- target shape: public instance `void ReadyToStart()` with no arguments;
- patch owner: `AuraDirector.Shared.ReadyToStart.Harmony.v1`.

An unknown assembly hash is rejected as `detour-target-build-unverified`.
Installation failure leaves the original method enabled.

## Verified Behavior

The automated gate verifies the known-build catalog, suppression and one-shot re-entry, duplicate hold
and release handling, sink failure-open behavior, teardown release, patch owner
installation/removal for an allowlisted build, target fingerprint fail-closed
behavior, and Terrias-only binary packaging. It does not preserve UI layout,
private method order, or input implementation as source-string snapshots;
runtime presentation changes require the focused Terrias behavior checks and
in-game verification appropriate to their impact.

## Packaging

`Terrias-Dev/Terrias.Dll.csproj` builds the provider project and copies
`Aura.Director.DetourBackend.dll` plus `0Harmony.dll` to `Terrias/Scripts`.
The release test rejects those binaries from every other shipped MOD script
root. Multiplayer plan distribution is intentionally deferred; each client
owns only its local opening and local hold.

## Dependency References

- Harmony package: https://www.nuget.org/packages/Lib.Harmony/2.4.2
- Harmony Prefix behavior:
  https://harmony.pardeike.net/articles/patching-prefix.html
