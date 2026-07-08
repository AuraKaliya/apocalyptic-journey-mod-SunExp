# Shared Runtime Boundaries

Use this reference when changing shared components or their SunExp adapters.

## Component Roles

- Core service: stores shared config, registry, packages, operation logs, and
  reflected global component protocol. It does not know business semantics.
- Domain shared component: owns validation, identity, priority, fallback,
  conflict policy, and machine-readable results for one domain.
- Adapter: initializes Core, installs packages, registers manifests/providers,
  and delegates to a domain component.
- Utility helper: stateless or local helper with no shared persistent state.

When adding a shared component, decide which role it has before coding.
For the SunExp/AuraToolsExp split, also check
`content-tool-shared-boundary.md`: SunExp is the content mod, AuraToolsExp is
the tool mod, and shared runtimes are sibling foundations for both, not a
SunExp-owned base layer.

## Ownership And Mutability

Registered artifacts need stable `ownerModId` and owner-qualified identity.
Foreign artifacts may be selected, inspected, referenced, or copied, but not
edited as if they belonged to the current mod.

Conflict policy must be explicit. Do not treat two applicable artifacts as a
technical error unless the domain contract says so.

## Initialization Registration And Tool Overrides

Use a strict owner/tool split for cross-mod shared features.

Initialization registration is the startup phase where a mod declares the
resources, rules, providers, or extension metadata that it owns. This is not
content-mod-exclusive:

- SunExp registers SunExp-owned roles, resources, manifests, and MOD-unique
  content extensions.
- AuraToolsExp may register official-content extensions or tool-owned providers
  and declarations.
- Other mods may register their own resources and extension metadata.

Every registered artifact still needs a stable `ownerModId` and a stable
domain id. A tool mod may register what it owns, but it must not re-own a
foreign mod's resources.

Content mods own content. They install their resources into AuraShared, register
domain manifests, and provide the machine-readable semantics needed by
consumers: target roles, trigger/card ids, resource paths, presentation or
playback options, priority, enabled state, and display labels.

Tool mods consume shared declarations. They read shared registries, parse
entries by the domain protocol, display/manage them, register tool-owned
extensions, and may import entries as local editable configuration or
overrides.

Tool mods must not guess a content mod's folder layout, hard-code content mod
resources, scan private content folders as a substitute for registration, or
re-own foreign resources by copying them under the tool mod unless the user
explicitly creates a local override.

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
  audio pack, skin, or starter deck profile.
- Tool-local rule display names may come from registry entry display names.
- Stable ids (`roleId`, `cgId`, `resourceId`, `profileId`) must not depend on
  localized display text.

For Skill CG specifically, content mods should provide CG registry entries with
`cgId`, `displayName`, `kind`, `targetRoleIds`, `cardIds`, `media.resource`,
`defaultPresentation`, `priority`, and `enabled`. Tool mods may import these as
rules, but must keep the CG `displayName` on the rule, not on the role.

Skill CG playback is a shared presentation protocol, not a SunExp-private or
AuraTools-private feature. Keep multiplayer relay, sender authority, playback
identity, and cross-mod de-duplication inside `AuraCgShared`. Content mods such
as SunExp should install and register CG resources, match local trigger
semantics, and submit playback requests to the shared runtime. Tool mods such
as AuraTools should configure enablement, local rules, overrides, and imported
registry entries without creating a second network playback path.

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

## Tests

Keep architecture tests close to the contract. Shared tests currently scan for
raw shared writes, required documentation anchors, authority checks, and known
consumer contracts.
