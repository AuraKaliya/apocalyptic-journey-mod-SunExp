# Shared Component Architecture Audit

This audit checks the current shared components against
`docs/shared-component-architecture-guidelines.md`. The StarterDeck profile work
is treated as the reference shape for a domain arbiter, but not every shared
component needs every StarterDeck rule.

## Rubric

- `Aligned`: follows the relevant rules for its component type.
- `Mostly aligned`: the main architecture is correct, with small documentation
  or identity gaps.
- `Needs follow-up`: a concrete rule is missing or weaker than the guideline.
- `Not a domain arbiter`: a utility/helper component where candidate ownership
  and priority rules do not apply.

## Results

| Component | Type | Status | Evidence | Follow-up |
| --- | --- | --- | --- | --- |
| `AuraSharedCore` | Foundation service | Aligned | Provides reflected global core, storage scopes, revision/authority checks, registry, package install, transaction recovery, operation logs, and change feed. It rejects generic registry conflicts by `system::resourceId` but does not parse domain semantics. | Keep Core semantic-free. Domain-specific priority, editability, and ownership inference must stay out of Core. |
| `StarterDeckArbiterShared` | Domain arbiter | Aligned | Defines `StarterDeckProfile`, source kind, owner-qualified identity, immutable registered profiles, shared registration, candidate sorting, effective resolution, validation result, role-owner inference, and deck application markers. | Use as the reference implementation for future user-selectable domain registries. |
| `AuraAudioShared` | Domain adapter | Aligned | Installs shared audio packages through `AuraSharedPackageEngine`, initializes Core, then delegates provider registration to `AudioArbiterShared`. | Keep it as an adapter; do not move sound matching logic here. |
| `AudioArbiterShared` | Runtime domain arbiter | Aligned | Owns `audio.registry.json`, provider models, manifest schema checks, owner-qualified provider identity, priority sorting, `hardClaim`, cooldowns, sync policy, original suppression, request resolution, and global compatibility checks. | Keep requests compatible with both bare and owner-qualified provider ids. |
| `BattleBgmArbiterShared` | Runtime domain arbiter | Mostly aligned | Provides global BGM arbiter, protocol/build compatibility, owner-qualified provider identity, provider registration, priority ordering, hard-claim behavior, battle/adventure context, and deterministic fallback to original/silence behavior. | Add a manifest protocol only if external Mods are expected to register BGM data declaratively. |
| `AuraCgShared` | Runtime domain arbiter | Mostly aligned | Provides global CG arbiter, build/protocol/method-shape compatibility, owner-qualified provider identity, provider registration, priority ordering, queueing, duplicate windows, remote sync event, and shared-path image resolution. | Add a manifest protocol only if CG providers become data-registered. |
| `AuraSkinShared` | Domain service and installer | Aligned | Uses Core package installation for skin resources, validates package sources, keeps selection in shared Skin config, reloads shared registry, validates remote selection by content hash, and has protocol/build compatibility checks. | Selection is intentionally a shared user preference, not an owner-owned registered artifact. If future Skin profiles become editable packages, add explicit read-only/copy rules. |
| `AuraJourneyShared` | Domain protocol/state service | Aligned | Defines journey/route/node/state models, normalizes `JourneyId` to `ownerModId:localJourneyId`, stores definitions and runtime state through Core config, registers definitions in Core registry, keeps reducers/conditions pure, and rejects non-authority commits. README explicitly bans concrete Mod content ids in shared code. | Keep short-id reads as legacy compatibility only. |
| `AuraOnlineShared` | Feature shared library | Needs follow-up before broad sharing | Provides local chat store, catalog validation, encrypted catalog loading, content normalization, and sticker registry. It is not currently a Core-backed cross-Mod registry or global reflected component. | If this becomes a shared cross-Mod runtime, add owner/authority docs, protocol/build compatibility, and Core-backed persistence or an explicit reason for staying in-memory. |
| `AuraLogShared` | Utility adapter | Not a domain arbiter | Initializes Core and exposes owner log paths and enumeration through `AuraSharedLogStore`; `AuraLogFileWriter` is an explicit log sink. | Raw file writing is acceptable here because it is append/log output, not shared configuration or registry mutation. |
| `UiTransitionGuardShared` | Utility runtime guard | Not a domain arbiter | Provides a global transition guard with protocol/build compatibility checks and reflected method reuse. No registered content, candidate set, or user-editable data exists. | No StarterDeck-style ownership rule needed. Keep it stateless except runtime guard state. |
| `UiRaycastSafetyShared` | Utility helper | Not a domain arbiter | Provides stateless UI raycast disable/destroy/scrub helpers with no persistent shared state or cross-Mod registered artifacts. | No additional alignment needed. |

## Cross-Cutting Findings

Raw shared writes are currently concentrated in `AuraSharedCore` storage/package
internals and `AuraLogShared` log writing. That matches the guideline: domain
components should not bypass Core for shared config, registry, or package
mutation.

The strongest alignment pattern is:

1. install or register durable resources through Core;
2. keep domain validation and resolution inside the domain component;
3. let product Mods supply concrete content, UI choices, and live game context.

Audio, BGM, and CG now use owner-qualified provider identity internally. A bare
provider id remains a compatible request alias, but registration replacement is
scoped to the same owner-qualified provider id.

## Recommended Follow-Ups

1. Revisit `AuraOnlineShared` only when it is intended to become a persistent
   shared service rather than an in-memory feature library.
2. Add data manifest protocols for BGM or CG only when those domains need
   declarative cross-Mod registration.
