# Native Synthetic Runtime Objects

Use this reference when Terrias creates or drives a synthetic combat object
that is expected to behave like a native Role, Partner, Enemy, status, or
ScriptExecutor owner. Current examples include Projection and Spirit, but the
workflow is defined around native contracts rather than those feature names.

## Evidence Order

- Use the newest applicable decompile snapshot to understand host control flow,
  advice/interceptors, return-value meaning, owner indexes, and cleanup.
- Use repository `Managed/` assemblies as the compilation contract.
- Use runtime logs to build an asymmetry matrix: which target/effect worked,
  which was absent, and whether the top-level transaction still reported
  success.
- Revalidate behavioral findings when the relevant Witch/Witch.Core binaries
  change. Keep versioned paths and line numbers in the game-reference index or
  incident notes, not in this operational reference.

## Native Equivalence Checklist

Creating one object or registering one manager entry does not establish native
equivalence. Inventory every surface used by the native object type:

- stable instance/status identity and data/config identity;
- owning Role/player/status relationship;
- manager and lookup registration;
- native owner maps or secondary indexes;
- action queue and turn-order participation;
- ScriptExecutor `Self`, target, current `status`, and target collection;
- local-versus-remote/network routing;
- UI, position, intent, and presentation registration when applicable;
- death, summon failure, cancellation, reconnect, battle-end, and scene cleanup.

Repair the earliest missing surface. Do not compensate downstream by copying
Buffs, replaying effects, or creating a second sync channel while the native
ownership path can represent the object correctly.

## Current Partner Locality Contract

For the current host runtime, native Partner creation registers the Partner
status under its owner in `TempDataManager.RoleStatusMap`. Native
`ForEachObject` changes `ScriptExecutor.status` for each target, then
`TrySendOnlineEvent` uses that status membership to decide locality.

The boolean is a routing result: `true` means the event was sent and local
mutation is skipped; it does not mean the target effect was applied locally.
Therefore a synthetic Partner missing from the correct owner list can produce
enemy damage and a successful actor turn while silently missing its own local
Buff mutation.

For current synthetic Partner-derived objects:

- register the status exactly once under the actual owner;
- remove duplicates and stale entries under previous owners during repair;
- make repeated registration idempotent without reordering an already correct
  list;
- remove the status from every owner list on failed initialization, death,
  cancellation, battle cleanup, or identity removal;
- validate this contract against the newest host behavior before carrying it
  across a game-version change.

`Vars["Online"]` is a native executor-wide routing override. Do not create,
clear, or overwrite it to repair one synthetic object's locality: that changes
the routing of every target, including enemies that should still use native
network distribution. If a native caller supplied the key, preserve its exact
pre-existing value.

## Script Execution Scope

Synthetic card or action execution must be a transaction around every native
script phase that depends on executor identity, including initialization,
refresh/draw/drop, attachment pre-use/use, and main use where applicable.

- Save the exact original `Self`, `Target`, current `status`, and `Object`
  collection reference before mutation.
- Bind the synthetic self and intended targets, clear transient current status
  when the native phase expects to choose it per target, and use a fresh target
  list when isolation is required.
- Restore the exact saved values and collection reference in `Dispose`/`finally`
  on success, failure, and exception.
- Do not clear unrelated executor Vars or invent routing flags.
- If entering the scope fails partway, restore everything already changed
  before propagating failure.

## Diagnostics

Log state transitions, not every frame. Useful fields include:

- synthetic status id and expected owner Role id;
- current owner-map membership and duplicate/stale owner count;
- self id, per-target status id, and target category;
- expected locality or RPC branch;
- action/turn token, battle generation, and cleanup source;
- exact initialization or scope phase that failed.

A top-level `Completed` action is not proof that each target effect executed.
Acceptance logs must include at least one local self effect and one target that
retains its native remote/network route.

## Behavior Test Matrix

- correct registration, idempotent re-registration, stale-owner migration, and
  duplicate repair;
- failed initialization rollback and all lifecycle cleanup paths;
- synthetic self classified as local for owner-scoped mutation;
- enemy or other non-local target still classified for native RPC;
- normal and exceptional executor-scope restoration using the exact original
  object/list identities;
- preservation of an existing native `Vars["Online"]` value and proof that the
  synthetic path does not invent one;
- main card plus attachment phases under the same synthetic identity contract;
- an asymmetry regression where the actor can report successful actions while
  one target-side effect would otherwise be skipped.

Keep these as observable C# behavior tests. Architecture gates may enforce
layer placement and routed APIs, but must not snapshot private method names or
decompiled source text.
