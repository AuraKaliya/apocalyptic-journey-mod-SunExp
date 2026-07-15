# Aura Director Detour Technical Validation

## Decision

The Harmony Detour route is technically viable for the current game build.
This is a conditional Go for implementing the director runtime behind an
optional provider. It is not approval to ship or auto-enable the dependency in
every MOD.

## Scope

The probe patches only the public, argument-free
`FightManager.ReadyToStart()` method. It does not read or mutate `readyCount`,
`fightType`, `ActionQueue`, `DOAllAction`, or any other private progression
surface.

The backend lives in `AuraDirectorDetour-Dev` and depends on Lib.Harmony 2.4.2.
`Aura.Shared.dll` contains only the backend-independent hold/sink contracts and
does not reference Harmony or the optional backend assembly.

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

The automated probe verifies:

1. a Harmony Prefix suppresses the first original call;
2. duplicate calls while held share one hold;
3. releasing re-enters the public method through a one-shot bypass;
4. the original executes exactly once;
5. duplicate release is idempotent;
6. a rejected or throwing sink fails open;
7. shutdown releases all outstanding holds before unpatching;
8. unpatching restores the original method;
9. Harmony installs on and uninstalls from the current real game method;
10. the backend never enters the production `Aura.Shared.dll` dependency graph.

## Remaining Runtime Work

The probe does not yet play CG, block input, build a battle cast, distribute a
plan, synchronize network time, or package `0Harmony.dll`. Those belong to the
next runtime integration phase.

Before production packaging, the provider must add battle-session ownership,
timeout release, scene/disconnect cleanup, multiplayer plan agreement,
conflicting-patch diagnostics, and an explicit enable/disable policy. Only one
provider may own the global patch.

## Dependency References

- Harmony package: https://www.nuget.org/packages/Lib.Harmony/2.4.2
- Harmony Prefix behavior:
  https://harmony.pardeike.net/articles/patching-prefix.html
