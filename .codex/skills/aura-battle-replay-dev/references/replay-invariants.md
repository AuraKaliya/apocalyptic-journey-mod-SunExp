# Replay Invariants and Recurrent Failure Classes

Use this reference when designing or reviewing a repair. Each rule records the
counterfactual decision it is meant to change and its applicability boundary.

## Journal Time and Durability

Invariant: truth events keep monotonic logical state time; presentation events
keep their actual observed time even if actor/owner binding makes them arrive at
a higher sequence later. Open transactions and mutable motion tracks hold back
the durable prefix.

This prevents a late MOD presentation from being “fixed” by rewriting earlier
event time or by persisting an event that can still change. It does not permit
truth time regression or reordering causal sequence.

Enforce with reducer/finalizer tests for late presentation, truth regression,
open transactions, mutable tracks, crash recovery, and identical final roots.

## Shared Extension JSON

Invariant: the shared presentation boundary accepts valid JSON objects, rejects
duplicates/trailing content/depth or budget overflow, and recursively emits the
same canonical form used by the replay document. Arrays retain producer order.

This prevents every content MOD from manually sorting properties and prevents a
valid semantic payload from being rejected only at finalization. It does not
canonicalize arbitrary display strings or reorder arrays.

Enforce in shared behavior tests and independently in final document validation.

## Pooled Presentation Completion

Invariant: a pooled visual has a generation-scoped presentation lifetime.
Completion comes from the shared reset owner or exact inactive/destroy/rebind
evidence, not from object reachability. Root, source instance, and generation
must agree.

This prevents long watchdog failures after a card has visibly finished and
returned to a pool. It does not replace normal destruction for genuinely
non-pooled temporary views.

Enforce a matrix for reset, wrong root/source, inactive, destroyed, rebind,
duplicate completion, reuse, and true watchdog expiry.

## Native Card Versus Intent Identity

Invariant: the native data table is authoritative. `Card` owns card artwork,
frame, text, and native CardItem projection. `EnemyCard` and `PartnerCard` own
intent foreground/background projection and `Intent` transactions.

This prevents an action icon from being validated as required card artwork. It
applies to native action sources, not owner-qualified extension module types.

Enforce the data-type classifier, exact catalog routing, and finalizer
cross-check between transaction kind and descriptor catalog. Unknown types fail
recording rather than falling through to `Card`.

## Resource Resolution and Sealed Documents

Invariant: record resolved resource identity at capture time and fail closed
when a required playback resource or provider is unavailable. Do not use a
playback-time heuristic to change content kind or invent lost fields.

This prevents a downstream fallback from hiding a writer error. Optional
resources may still use an explicitly documented deterministic fallback before
the document is sealed.

Sealed roots remain immutable. A migration is allowed only when it is bounded,
one-way, lossless, revalidated, and cut over from the old runtime path.

## Renderer and First-Frame Safety

Invariant: renderer ownership, renderer-feature compatibility, target ownership,
actual RenderGraph execution, pixel validity, normal-frame survival, and
teardown are distinct gates.

This prevents reflection tests, a cloned feature list, or a successful
`Camera.Render` return from being treated as sufficient. It also prevents a
post-render pixel test from being described as crash containment.

Do not turn this into “remove all features.” Native UI/shader correctness may
require some renderer capabilities. Establish the smallest explicit replay
renderer profile whose camera inputs and RenderGraph resources satisfy every
retained feature. Retained Feature ScriptableObjects are deep-cloned and owned
by the replay renderer; excluded features have an explicit semantic reason;
unknown active features reject the profile. Leave the game's renderer data,
Feature instances, and active state untouched.

## Cross-MOD Coverage

Invariant: AuraToolsExp automatically records native visible surfaces. Private
MOD state/layout/presentation is portable only through owner-qualified shared
providers/modules with declared schema, build, and renderer capability.

This prevents AuraToolsExp from reflecting arbitrary Terrias objects or making
Terrias an implicit shared framework. It does not require a provider for a MOD
object already represented completely by native Status/Card/Intent surfaces.

For Terrias, independently cover Spirit and Projection spawn, state, intent,
action, despawn, owner attachment, and provider-required renderer behavior.
