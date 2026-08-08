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

## Verified Capability

- target assembly: `Managed/Witch.dll`;
- current game reference: `v1.0.24605918` (assembly evidence SHA-256
  `C8D9B8B0E3B553B01464F6F3909A3C360C19B83BDD7AC0488F18B29631872B68`);
- capability profile: `ReadyToStartGate.V1`;
- verified method-body SHA-256:
  `5BC8DA8FF9659712B6CA63AC833CF23F00414265BC880444849881B097CE9CB6`;
- target shape: public instance `void ReadyToStart()` with no arguments;
- patch owner: `AuraDirector.Shared.ReadyToStart.Harmony.v1`.

The assembly hash is retained only as audit evidence. Compatibility is decided
by the target method shape and method-body capability fingerprint, so an
unrelated game assembly change does not revoke a valid capability. An unknown
method body is rejected as `detour-target-capability-unverified`. Installation
failure leaves the original method enabled.

## Foundation Model Trust

Foundation-model trust is independent from the game build and hook capability.
The shipped catalog `AuraToolsExp/Config/aura-director.foundation-model-allowlist.json`
authorizes a model lineage (`Aura.Foundation.V1` or `Aura.Foundation.V2`) by
artifact or weight SHA-256 plus its feature schema, content set, ruleset, native
program package, and required start-gate capability. A trusted hash with a
mismatched compatibility tuple is rejected.

## Verified Behavior

The automated gate verifies the method-capability catalog, suppression and one-shot re-entry, duplicate hold
and release handling, sink failure-open behavior, teardown release, patch owner
installation/removal for an allowlisted capability, target fingerprint fail-closed
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
