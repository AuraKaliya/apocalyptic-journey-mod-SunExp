# Aura Director v1 Local Runtime

## Decision

The shared director lives in `AuraDirectorShared` and is packaged into
`Aura.Shared.dll`. Terrias enables the reviewed Harmony start-gate provider for
the verified game build and ships its provider binaries only in
`Terrias/Scripts`. The local runtime component protocol is version 2. The
deterministic plan envelope is schema version 2 and can read schema version 1.
Requests and compiled plans carry a stable contract id, schema/read-range,
bounded extension fields, and a self-contained descriptor plus cue envelope.
The plan hash covers those compatibility fields in deterministic key order.

The first production scope is local-first: every client independently builds
and plays its own opening. There is no director RPC, peer plan agreement, or
host-owned playback in this version. Native `ReadyToStart` synchronization
still provides the eventual battle-start barrier between peers.

## Runtime Flow

1. The provider intercepts the local `FightManager.ReadyToStart()` call.
2. `TerriasBattleOpeningRequestSource` builds the local player and current
   `EnemyManager.enemyList` cast and explicitly selects the side-portrait v2
   strategy.
3. `AuraDirectorPlanCompiler` validates the request, stably groups friendly
   actors before hostile actors, preserves order inside each side, and creates
   deterministic portrait, letterbox, and wait cues. Neutral actors follow the
   two battle sides.
4. The side-portrait v2 plan starts its first cue after a 0.3-second unscaled
   delay. A transparent screen-space input shield is active during the delay;
   the visible overlay appears only when playback starts.
5. Friendly portraits enter from screen right, focus at the left third, and
   exit through screen left. Hostile portraits use the mirrored route through
   the right third.
6. Actor portraits use their current battle-body sprite mesh. The presenter
   preserves native renderer flips, recenters asymmetric mesh bounds, and fits
   the visible mesh height between the expanded letterbox edges with 10 pixels
   of clearance above and below. Height takes priority for wide actors; their
   offscreen slide positions account for the focused portrait width.
7. Letterbox expansion and relaxation run concurrently with portrait entry and
   exit, and the compiled wait cue supplies the inter-actor gap.
8. Escape, Space, Enter, Numpad Enter, or the left mouse button skip through
   Unity's Input System. Skip debounce begins when visible playback starts. A
   polling failure disables skip input once without interrupting playback or
   native release.
9. A generated generic silhouette is used when the body or sprite cannot be
   resolved.
10. Completion, user skip, timeout, destroyed battle target, or runtime teardown
   releases the native hold exactly once.

The overlay advances with `Time.unscaledTime`; it never changes
`Time.timeScale`. Terrias scales the hard timeout with cast size from 12 to 30
seconds, and the compiler clamps all requests to the shared 5-60 second safety
range.

## Compatibility Boundary

The runtime and optional start-gate provider advertise overlapping protocol
ranges instead of requiring exact version equality. A provider is installed
only when its contract id matches and its range overlaps the local runtime.
The plan compiler accepts only the supported schema range, rejects readers
newer than itself, preserves bounded unknown extensions, and rejects oversized
or malformed extension maps. Enum and cue behavior remain owned by the
compiler; extensions cannot silently acquire execution authority.

The default strategy is `side-portrait-v2` with profile `opening-side-v2`.
Explicit `alternating-portrait-v1` requests remain supported with their original
caller order, centered focus, alternating directions, and no opening delay.

## Fail-Open Boundary

An unverified `Witch.dll`, provider conflict, patch failure, missing cast,
compile rejection, overlay construction failure, or request-source exception
returns control to the original `ReadyToStart()` call. The runtime does not
read or mutate private `readyCount`, `fightType`, or `ActionQueue` state.

## Current Scope

Enabled:

- local player plus current enemy cast;
- 0.3-second unscaled pre-roll with transparent input blocking;
- friendly-first stable side grouping and mirrored one-third focus routes;
- native battle sprites with generic silhouette fallback;
- cue-driven letterbox expansion, relaxation, and inter-actor waits;
- mesh-bound portrait focus with 10-pixel vertical clearance;
- native flip preservation and fully offscreen wide-portrait slide endpoints;
- Input System keyboard and mouse skip handling;
- local input blocking, skip, timeout, and cleanup;
- feature switch `Terrias/Battle.OpeningDirector`;
- Terrias-only provider packaging.

Deferred:

- synchronized director plans and clocks;
- late join and reconnect playback;
- content-owned portrait providers and authored cue profiles;
- migration of Skill CG into the director timeline.
