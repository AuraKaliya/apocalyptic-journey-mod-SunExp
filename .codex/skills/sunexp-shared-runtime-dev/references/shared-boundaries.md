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

## Ownership And Mutability

Registered artifacts need stable `ownerModId` and owner-qualified identity.
Foreign artifacts may be selected, inspected, referenced, or copied, but not
edited as if they belonged to the current mod.

Conflict policy must be explicit. Do not treat two applicable artifacts as a
technical error unless the domain contract says so.

## Content Mods And Tool Mods

Use a strict content/tool split for cross-mod shared features.

- Content mods own content. They install their resources into AuraShared,
  register domain manifests, and provide the machine-readable semantics needed
  by consumers: target roles, trigger/card ids, resource paths, presentation or
  playback options, priority, enabled state, and display labels.
- Tool mods consume shared declarations. They read shared registries, parse
  entries by the domain protocol, display/manage them, and may import entries as
  local editable configuration or overrides.
- Tool mods must not guess a content mod's folder layout, hard-code content mod
  resources, scan private content folders as a substitute for registration, or
  re-own foreign resources by copying them under the tool mod unless the user
  explicitly creates a local override.
- Domain shared layers own manifest schemas, normalization, conflict policy,
  and compatibility. AuraSharedCore remains semantic-free.

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

Shared components that advance state must identify the authority writer. Client
presentation is allowed; shared progression, registry mutation, and shared
runtime state should be host/server-authoritative.

For server-bound RPC, bind the sender from the server receive context rather
than trusting payload fields such as reporter, issuer, or role owner. Centralize
authorization in a policy/runtime layer and pass the bound sender into command
application.

Payload transports should enforce byte budgets before Mirror serialization.
Large shared payloads should use a bounded chunked-transfer path with checksum,
expiration, and a cap on active receiver buffers.

## Tests

Keep architecture tests close to the contract. Shared tests currently scan for
raw shared writes, required documentation anchors, authority checks, and known
consumer contracts.
