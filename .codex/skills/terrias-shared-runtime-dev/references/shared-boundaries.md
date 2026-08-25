# Shared Runtime Boundaries

Use this reference when changing shared components or their Terrias adapters.

## Component Roles

- Core service: stores shared config, registry, packages, operation logs, and
  reflected global component protocol. It does not know business semantics.
- Domain shared component: owns validation, identity, priority, fallback,
  conflict policy, and machine-readable results for one domain.
- Adapter: initializes Core, installs packages, registers manifests/providers,
  and delegates to a domain component.
- Utility helper: stateless or local helper with no shared persistent state.

When adding a shared component, decide which role it has before coding.
For the Terrias/AuraToolsExp split, also check
`content-tool-shared-boundary.md`: Terrias is the content mod, AuraToolsExp is
the tool mod, and shared runtimes are sibling foundations for both, not a
Terrias-owned base layer.

## Ownership And Mutability

Registered artifacts need stable `ownerModId` and owner-qualified identity.
Foreign artifacts may be selected, inspected, referenced, or copied, but not
edited as if they belonged to the current mod.

Conflict policy must be explicit. Do not treat two applicable artifacts as a
technical error unless the domain contract says so.

Classify mutable runtime conflicts before choosing an implementation. Persistent
selection uses precedence, replicated state uses an authoritative versioned
snapshot, additive contributions use aggregation, and nested temporary
mutations may require an ownership stack. When sibling consumers temporarily
mutate the same Unity/runtime property, load
`shared-mutable-runtime-ownership.md`; do not let each consumer capture and
restore its own baseline.

## Initialization Registration And Tool Overrides

Use a strict owner/tool split for cross-mod shared features.

Initialization registration is the startup phase where a mod declares the
resources, rules, providers, or extension metadata that it owns. This is not
content-mod-exclusive:

- Terrias registers roles, gameplay declarations, and required content
  presentation from its runtime. Terrias-carried optional voice/CG remains
  declarative under `SharedResources` and is discovered by AuraToolsExp.
- AuraToolsExp may register official-content extensions or tool-owned providers
  and declarations.
- Other mods may register their own resources and extension metadata.

Every registered artifact still needs a stable `ownerModId` and a stable
domain id. A tool mod may register what it owns, but it must not re-own a
foreign mod's resources.

Content mods own gameplay content, stable semantic ids, and may carry their own
optional voice/CG declarations and files. AuraToolsExp owns discovery and local
effective configuration; it continues to own replacement skins, card themes,
configurable effects, and tool-default media.

Temporary consumers such as native replay must use owner-qualified,
non-persistent scoped skin selections with disposable handles. They must not
rewrite `SkinSelectionStore` or leave a replay override active after teardown.

Tool mods consume shared declarations. They read shared registries, parse
entries by the domain protocol, display/manage them, register tool-owned
extensions, and may import entries as local editable configuration or
overrides.

Tool mods must not guess private folder layouts or depend on content runtime.
They may read only the fixed `SharedResources/aura.discovery.json` contract;
the scanner validates contained paths, binds the source to the root `.modproj`
id, and preserves the declaration's semantic owner.

Keep registered defaults separate from tool-local effective configuration.
Content-owned declarations are usually enabled by default. AuraToolsExp may
read local persistent configuration and force the effective tool state, using
this precedence:

`registered default -> tool shipped default -> local persistent override`

The local override changes tool-side effective behavior; it must not mutate the
foreign registry source or claim ownership of another mod's artifact.

Domain shared layers own manifest schemas, normalization, conflict policy, and
compatibility. AuraSharedCore remains semantic-free.

Keep display semantics separated:

- Role display names describe roles only.
- Registry entry display names describe registered artifacts such as a skill CG,
  audio pack, or skin. AuraTools custom-start files remain tool-local imports.
- Tool-local rule display names may come from registry entry display names.
- Stable ids (`roleId`, `cgId`, `resourceId`, `profileId`) must not depend on
  localized display text.

For Skill CG specifically, the optional-media owner should provide registry entries with
`cgId`, `displayName`, `kind`, `targetRoleIds`, `cardIds`, `media.resource`,
`defaultPresentation`, `priority`, and `enabled`. Tool mods may import these as
rules, but must keep the CG `displayName` on the rule, not on the role.

Skill CG playback is a shared presentation protocol, not a Terrias-private or
AuraTools-private feature. Keep multiplayer relay, sender authority, playback
identity, and cross-mod de-duplication inside `AuraCgShared`. Terrias carries
its owner-qualified media declarations; AuraTools discovers them and creates
requests from shared card-action signals.
The multiplayer payload is limited to registered owner/provider/CG identities,
owner/action/session ids, and bounded sequencing data. Each peer resolves the
same local registry and resource package; raw media bytes, paths, bundle data,
and presentation parameters are not a transport contract.

When building UI for tool-managed shared entries:

- Show the role name on role rows.
- Show the artifact/rule name on rule rows.
- Name directory buttons by target, such as "local directory", "image
  directory", or "resource directory"; avoid generic labels whose target is
  ambiguous.
- Open the actual resource directory for foreign shared resources instead of a
  tool-owned default import directory.
- Expose domain presentation/playback fields that the registry supports rather
  than requiring users to edit JSON by hand.

When building temporary overlays or transition visuals:

- Prefer an independent `ScreenSpaceOverlay` `Canvas` over parenting under
  `UIManager.upperCanvasTf`; the game's upper canvas controller can treat
  lingering children as active upper UI and leave native screens unclickable.
- Omit `GraphicRaycaster` for visual-only overlays. Keep all overlay graphics
  `raycastTarget = false`; keep root `CanvasGroup.blocksRaycasts = false` and
  `interactable = false`.
- Put coroutine drivers on a separate always-active runner, not on an overlay
  that may be hidden or destroyed.
- On close, disable raycasts, hide, clear children, scrub Unity's
  `GraphicRegistry` for several frames, and destroy temporary overlay roots
  instead of leaving inactive objects in native UI trees.
- During validation, inspect `UiTransitionGuard` restore logs and manually
  click the next native UI after the overlay closes.

## Compatibility

Shared components that create a persistent global `GameObject` component should
expose protocol/build/min-version compatibility. An incompatible existing global
component should disable the shared service for that consumer and log the
reason; it should not crash unrelated initialization.

Changing provider identity semantics requires a build/protocol bump.

## Multiplayer Authority

Classify multiplayer behavior before choosing an RPC shape:

- Shared progression, map state, run counters, and shared reward state are
  host/server-authoritative. Clients may request; the host validates and
  broadcasts a snapshot or result.
- Player-scoped rewards and choices are independent per player. Each client may
  show its own UI and apply its own local reward; persist only the player
  scoped result record unless the design explicitly says the reward is shared.
- Presentation events, overlays, CG playback, temporary visuals, and UI cleanup
  are not progression state. They may be synchronized, but they still need
  lifecycle cleanup and duplicate suppression.

Battle terminal ordering is a shared lifecycle contract. `OutcomeEntering`
closes transient producers, `BattleSettling` prepares the authoritative result,
`BattleEnded` lets every consumer release queues and Unity objects, and
`BattleFinalized` is the ordered post-cleanup snapshot barrier. Consumers must
not infer this ordering from registration order.

Shared components that advance state must identify the authority writer. Client
presentation is allowed; shared progression, registry mutation, and shared
runtime state should be host/server-authoritative.

For server-bound RPC, bind the sender from the server receive context rather
than trusting payload fields such as reporter, issuer, or role owner. Centralize
authorization in a policy/runtime layer and pass the bound sender into command
application.

For presentation protocols that accept client-originated requests, the host must
bind the real sender, validate that the sender owns the submitted owner/status,
then relay an authorized event. Remote observation hooks must not generate new
authoritative event ids. If a local owner/status id is missing in multiplayer,
skip the presentation request and log a diagnostic instead of broadcasting an
ambiguous event.

Payload transports should enforce byte budgets before Mirror serialization.
Large shared payloads should use a bounded chunked-transfer path with checksum,
expiration, and a cap on active receiver buffers.

## Background Work

`AuraSharedFrameScheduler` is a main-thread scheduler. Its actions may touch
Unity, Witch, Mirror, UI, or game state and must not be moved to worker threads.
Use `AuraSharedBackgroundWorkScheduler` only for immutable snapshots, pure CPU
transforms, and file work. It has local CPU/IO concurrency and owner-pending
caps; do not change the process-wide CLR ThreadPool limits. Worker results must
return through the scheduler completion queue, then apply on the main thread
after a current-generation check. Coroutines split main-thread work across
frames; they are not a replacement for background CPU work.

## Tests

Keep architecture tests close to the contract. Shared tests currently scan for
raw shared writes, required documentation anchors, authority checks, and known
consumer contracts.
