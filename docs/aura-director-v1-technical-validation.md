# Aura Director v1 Local Runtime

## Decision

The shared director lives in `AuraDirectorShared` and is packaged into
`Aura.Shared.dll`. SunExp now enables the reviewed Harmony start-gate provider
for the verified game build and ships its provider binaries only in
`SunExp/Scripts`.

The first production scope is local-first: every client independently builds
and plays its own opening. There is no director RPC, peer plan agreement, or
host-owned playback in this version. Native `ReadyToStart` synchronization
still provides the eventual battle-start barrier between peers.

## Runtime Flow

1. The provider intercepts the local `FightManager.ReadyToStart()` call.
2. `SunExpBattleOpeningRequestSource` builds the ordered cast from the local
   player followed by `EnemyManager.enemyList`.
3. `AuraDirectorPlanCompiler` validates the request and creates deterministic
   alternating portrait cues.
4. A screen-space overlay blocks local input and progression while cues play.
5. Actor portraits use their current battle-body sprite. A generated generic
   silhouette is used when the body or sprite cannot be resolved.
6. Completion, user skip, timeout, destroyed battle target, or runtime teardown
   releases the native hold exactly once.

The overlay advances with `Time.unscaledTime`; it never changes
`Time.timeScale`. SunExp scales the hard timeout with cast size from 12 to 30
seconds, and the compiler clamps all requests to the shared 5-60 second safety
range.

## Fail-Open Boundary

An unverified `Witch.dll`, provider conflict, patch failure, missing cast,
compile rejection, overlay construction failure, or request-source exception
returns control to the original `ReadyToStart()` call. The runtime does not
read or mutate private `readyCount`, `fightType`, or `ActionQueue` state.

## Current Scope

Enabled:

- local player plus current enemy cast;
- native battle sprites with generic silhouette fallback;
- local input blocking, skip, timeout, and cleanup;
- feature switch `SunExp/Battle.OpeningDirector`;
- SunExp-only provider packaging.

Deferred:

- synchronized director plans and clocks;
- late join and reconnect playback;
- content-owned portrait providers and authored cue profiles;
- migration of Skill CG into the director timeline.
