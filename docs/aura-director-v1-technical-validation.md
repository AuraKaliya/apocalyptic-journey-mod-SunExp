# Aura Director v1 Technical Validation

## Decision

The shared director belongs in `AuraDirectorShared` and is packaged into
`Aura.Shared.dll`. The official Mod hook remains unable to gate progression.
An isolated Harmony backend has now passed technical validation for the current
game assembly, but is not packaged or enabled in a production MOD yet.

## Verified Native Path

The current battle startup path is:

1. `FightManager.Init` constructs player and enemy action state.
2. Every peer calls `FightManager.ReadyToStart`.
3. The server-side user code increments a private `readyCount`.
4. The server changes the fight type to `FightType.Start`.
5. `Fight_Start.Init` starts `FightManager.DOAllAction`.

The current `Witch.Core.ModHookContext` exposes only `Target` and `Arguments`.
`Modifiable` invokes before/after callbacks as observational actions, catches
callback exceptions, and does not expose Rougamo's `MethodContext` or its
`ReplaceReturnValue` control to Mods. Therefore a Mod cannot cancel or defer
`ReadyToStart` through the supported hook API.

## No-Go Boundary

The following workarounds are explicitly rejected:

- mutate private `readyCount`;
- rewrite `fightType` or `ActionQueue`;
- stop native coroutines;
- write `Time.timeScale`;
- introduce Harmony, MonoMod, or another detour dependency without a separate
  reviewed decision.

`AuraDirectorNativeStartBarrierProbe.Probe()` records this capability as
`native-hook-not-cancellable`. An incompatible backend must fail open and must
not interfere with unrelated shared initialization.

## Preserved Development Output

The safe, backend-independent v1 contract remains implemented:

- normalized serializable actor/resource/request models;
- deterministic `alternating-portrait-v1` cue compilation;
- regular and compact timing profiles;
- actor-count, identity, resource, and strategy validation;
- deterministic plan hashing for future peer comparison;
- idempotent director-session release state.

No battle-start invoker, visual overlay, input lease, network barrier, or Skill
CG migration is enabled while the progression gate is unsupported.

## Resume Gate

Runtime integration may resume only through one of these verified paths:

1. the game exposes a cancellable/replaceable Mod hook for the startup call;
2. the game exposes a supported pre-start readiness extension point; or
3. the isolated, capability-probed Detour backend validated in
   `aura-director-detour-validation.md` is explicitly promoted from technical
   probe to a packaged provider after its runtime integration review.
