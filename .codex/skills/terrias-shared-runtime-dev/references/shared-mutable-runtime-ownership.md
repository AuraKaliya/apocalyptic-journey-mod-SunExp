# Shared Mutable Runtime Ownership

Use this reference when two or more sibling consumers temporarily mutate the
same Unity object or runtime property, especially pooled presentation roots,
Renderers, materials, sprites, overlays, or scheduled visual state. It defines
the semantic-free ownership model; feature-specific renderer selection and
content meaning stay in the owning domain reference.

## Choose The Conflict Model First

Do not use one conflict mechanism for every kind of shared state:

- Persistent selection or local configuration uses explicit precedence and an
  authoritative effective value.
- Authoritative replicated state uses versioned snapshots and one writer.
- Independent additive contributions use aggregation or arbitration.
- Nested temporary mutations of one mutable target use a coordinated ownership
  stack when later layers must restore earlier layers.

The rules below apply to the final category. Do not introduce a stack merely
because several consumers can observe the same object.

## Identity And Authority

Key a temporary mutation by all identities that can invalidate restoration:

`physical root + logical generation + exact target/property`

- The physical root distinguishes Unity instances.
- The logical generation distinguishes separate uses of a pooled instance.
- The exact target/property prevents one Renderer or field from borrowing the
  baseline of another.

One shared coordinator owns the baseline and all restoration decisions.
Consumers receive handles that represent requests; they do not independently
capture an "original" value or restore a value observed while another
temporary owner was active.

## Acquire And Release Contract

- Capture the native baseline once, before the first successful temporary
  write for that identity.
- Publish a new owner only after the target write succeeds. If acquisition
  partially mutates the target, roll it back before returning failure.
- If rollback cannot prove restoration, mark the identity faulted or dirty.
  Do not make it available for reuse.
- A lower owner may release out of order, but the coordinator records that
  release as pending. The handle must not destroy or forget resources still
  reachable from the ownership stack.
- Releasing the top owner drains every consecutively pending owner in LIFO
  order and restores the native baseline exactly once when the stack empties.
- Release and destruction callbacks are idempotent and run at most once for
  the resource identity they own. Callback failure must not corrupt stack
  cleanup.
- If the target was externally changed, destroyed, or belongs to another
  generation, never reattach a stale value. Quarantine or abandon the identity
  and clean coordinator bookkeeping without guessing.

## Deferred Work Is An Obligation

Any log or return value that says cleanup was deferred, skipped because a newer
owner exists, or handed off must answer all of these:

- Which coordinator or state record now owns the obligation?
- What durable pending state proves it was retained?
- Which release, lifecycle event, or scheduler action will retry or drain it?
- Which postcondition proves convergence?

Returning after a warning without those answers is abandoned cleanup, not a
valid defer path.

## Pooling Contract

Pooling is an optimization and must not weaken presentation correctness.

- Increment a logical generation for every activation, including reuse of the
  same Unity root and the same card id.
- Every asynchronous callback, scheduled action, presentation request, and
  temporary mutation handle must carry or validate that generation.
- Transition through explicit states such as idle, bound, suppressed, exiting,
  resetting, and destroyed. Do not infer readiness from `activeSelf` alone.
- Lightweight rebind is allowed only when object identity and every structural
  presentation dependency are unchanged. Include data, effective Vars, and
  derived description/style inputs; exclude only fields proven ephemeral, such
  as cost deltas handled by a dedicated refresh path.
- Reset consumers first, then require the coordinator to report the generation
  clean before returning the view to idle.
- If cleanup, rollback, or identity checks cannot prove cleanliness, destroy or
  quarantine the view instead of returning it to the pool.

## Diagnostics

State-transition diagnostics should be bounded and include enough identity to
reconstruct ownership without per-frame noise:

- root id and logical generation;
- exact target/property id;
- owner id, lease/token id, stack depth, and release disposition;
- previous and next lifecycle state;
- dirty, external-mutation, rollback, or abandonment reason;
- source lifecycle/action token when available.

## Behavior Test Matrix

Test the state machine rather than private class names or source snippets:

- ordinary nested acquire/release and native baseline restoration;
- every relevant out-of-order release permutation;
- duplicate release and duplicate cleanup callback;
- acquisition failure before write and after partial write;
- rollback failure followed by quarantine or destruction;
- external target mutation and destroyed target;
- cross-generation conflict on the same physical root;
- pool reset clean gate and dirty-view rejection;
- integration between at least two real consumers of the same target.

Keep the authoritative state-machine assertions in the shared/Core suite.
Consumer suites should prove focused integration and their own resource
cleanup, not duplicate the coordinator implementation.
