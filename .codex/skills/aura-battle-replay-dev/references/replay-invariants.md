# Replay Invariants and Recurrent Failure Classes

Use this reference when designing or reviewing a repair. Each rule records the
counterfactual decision it is meant to change and its applicability boundary.

## Journal Time and Durability

Current recording refinements:

- Hand acquisition has separate request, materialization and motion boundaries.
  `CreateCardItem` may only queue or reject a draw. Observe native DrawEffect
  before DrawScript can auto-use a view; commit hand state after actual creation
  and layout, without waiting for another action. Observe the existing cards'
  reflow as well as the incoming card. Finish at native tween completion and
  the measured hand pose; do not use an invented flight path or a fixed delay.
- For nested card creation, finalize a new view's name/cost snapshot only for
  the exact configuration passed to that completed native creation call.
  Other pending views may still be inside their own Init. Later draws must not
  rewrite a prior arrival snapshot. Completed/destroyed native cards do not
  re-enter the static hand simply because a native list still references them.
- New writers declare `observed-arrival-and-layout.v1` in the UI descriptor
  and require `observed-hand-arrival-and-layout.v1`. Missing arrival witnesses,
  repeated use of an old arrival for a redraw, and hand interaction before any
  appearance reject the document. Legacy records omit the marker and retain
  their original bytes; missing trajectories are not synthesized into them.

- Native `DoCardUseAnimation` leaves its centre CardItem's `dataConfig` null
  when `needInit=false`. Bind the one newly created direct centre child within
  the observed call, excluding earlier/claimed views. Its synchronous exit
  hook is part of that call, not a second visual. Reject ambiguous candidates.

- Observe actual hand dragging through release, return or native exit. Native
  attack cards retain their own targeting behavior. Physical visual identity
  distinguishes hand and centre views of one logical card; its static hand
  view is hidden while recorded motion owns the visible card.
- Preserve both ends of stationary intervals in compressed card/actor tracks.
  Removing all repeated poses makes interpolation move during a hold. Enter
  burn only at its observed phase and recognize the native shader, rather than
  any material exposing `_Fade`.
- Companion anchors use measured collider geometry and recorded owner pose.
  Legacy records retain their recorded idle geometry. Attack sprite extents
  are not anchors. Detached Spirit HUDs omit the additional element badge.
- Added nullable geometry/view identities omit absent fields from canonical
  JSON; documents containing them declare reader capabilities. Do not invent
  missing drag samples or collider measurements in retained documents.
- Capture batches retain an integrity hash. Provisional event/state hash chains
  are deferred until finalization after mutable tracks close. A prepared diff
  is reusable only at its original source state version.
- Transaction snapshots use independent typed copies; indexed transaction
  starts determine the durability watermark. Pin owner provenance per battle
  instead of scanning assemblies for every buff. Canonical serializers are
  local to their thread.

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
providers/modules with declared schema, portability and renderer capability.
Build identity records provenance; it is not a module compatibility version.
Match the owner/type and declared contract exactly. Breaking payload or renderer
changes must version that contract; unrelated recompilation must not invalidate
a sealed record. Keep recorded build identities and document roots unchanged.

This prevents AuraToolsExp from reflecting arbitrary Terrias objects or making
Terrias an implicit shared framework. It does not require a provider for a MOD
object already represented completely by native Status/Card/Intent surfaces.

For Terrias, independently cover Spirit and Projection spawn, state, intent,
action, despawn, owner attachment, and provider-required renderer behavior.

## Native UI Subtree Ownership

Removing a UI controller does not remove its authored graphics or initialization
requirements. Classify runtime-only owned subtrees while their components still
exist and remove those subtrees from the inactive replay clone before activation.
Native tutorial previews must not survive as masks, portraits or dialogue after
their owner is stripped. Match component identity rather than names or sibling
indexes; leave the original prefab and ordinary battle presentation intact.

Exercise this boundary in Unity with active, inactive, nested and renamed
owners, same-name ordinary graphics, first activation, teardown and reopening.

## Observed Geometry and Independent Tracks

Recorded screen/canvas coordinates must be converted through the playback
canvas before use under an offset or scaled native container. Preserve the
coordinate origin, native canvas scale contract, prefab pivot and observed
cost. A single mutable card view cannot represent overlapping trajectories.

Source metadata does not authorize an invented card animation: a skill can use
a Card data descriptor while remaining a skill action. Instantiate moving cards
only from their observed tracks. Bind actor sampling to its actual native
generation and resolved state; a global busy flag can retain unrelated actions.
Keep owner-attached module pulses independent from underlying status tracks.

Before removing a long quiet interval, verify its actor/round boundaries in the
record. Recorded player think time is not evidence of a lost enemy animation.
