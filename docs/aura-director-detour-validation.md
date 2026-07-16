# Aura Director Detour Validation

## Decision

The Harmony route is approved for the SunExp local director on the verified
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

## Verified Build

- game reference snapshot: `v1.0.23816797`;
- target assembly: `Managed/Witch.dll`;
- assembly SHA-256:
  `8D87696341625B19F63059B6D91262FF5738F3C0B5ABB7598A05C7640727790A`;
- target shape: public instance `void ReadyToStart()` with no arguments;
- patch owner: `AuraDirector.Shared.ReadyToStart.Harmony.v1`.

An unknown assembly hash is rejected as `detour-target-build-unverified`.
Installation failure leaves the original method enabled.

## Verified Behavior

The automated gate verifies suppression and one-shot re-entry, duplicate hold
and release handling, sink failure-open behavior, teardown release, patch owner
installation and removal, target fingerprinting, runtime timeout and cleanup
contracts, local cast construction, silhouette fallback, and SunExp-only binary
packaging. The source contract also rejects legacy `UnityEngine.Input` polling
and requires the Input System skip path, cue-driven letterbox playback, and the
10-pixel mesh-bound portrait layout. Pure layout assertions cover asymmetric
sprite bounds and height-priority wide portraits.

## Packaging

`SunExp-Dev/SunExp.Dll.csproj` builds the provider project and copies
`Aura.Director.DetourBackend.dll` plus `0Harmony.dll` to `SunExp/Scripts`.
The release test rejects those binaries from every other shipped MOD script
root. Multiplayer plan distribution is intentionally deferred; each client
owns only its local opening and local hold.

## Dependency References

- Harmony package: https://www.nuget.org/packages/Lib.Harmony/2.4.2
- Harmony Prefix behavior:
  https://harmony.pardeike.net/articles/patching-prefix.html
