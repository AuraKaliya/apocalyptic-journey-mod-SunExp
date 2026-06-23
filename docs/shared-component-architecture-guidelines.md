# Shared Component Architecture Guidelines

This document captures the shared-component rules that were clarified while
adding StarterDeck profile registration. Use it when adding or reviewing any
shared runtime, registry, package adapter, or domain arbiter.

## Layer Model

Shared features should stay in three layers:

1. `AuraSharedCore`: the bottom service layer.
2. Domain shared components: typed protocol, validation, aggregation, and
   resolution for one game-facing domain.
3. Product Mods and tools: concrete content, UI, user choices, and owner-local
   editable configuration.

Utility shared components may exist outside the domain-arbiter model when they
only provide stateless helpers or a narrow runtime guard.

## Core Rules

`AuraSharedCore` provides common services only:

- reflected global component discovery and compatibility checks;
- owner/shared/runtime storage with revision and authority checks;
- resource registry and package installation;
- locks, transaction recovery, change feed, and diagnostics.

Core must not know the business meaning of a domain. It must not decide which
starter deck, skin, BGM, CG, journey, or chat item wins. Core may reject a
generic resource conflict, but domain priority and fallback rules belong in the
domain shared component.

Shared writes should go through `AuraSharedConfigStore`,
`AuraSharedRegistry`, or `AuraSharedPackageEngine`. Raw file mutation belongs in
Core storage/package internals or in explicitly append-only utility sinks such
as logs.

## Domain Component Rules

A domain shared component should own the following surface:

- a stable schema or typed model for registered artifacts;
- owner identity and technical identity;
- registration from manifest or code into Core services when persistence or
  cross-Mod discovery is needed;
- validation with structured result objects or clear failure reasons;
- aggregation of candidates without silently collapsing semantic alternatives;
- centralized resolution or priority ordering;
- diagnostics that explain why a candidate was accepted, skipped, or rejected.

Domain components should keep game-context-specific hooks small. If validation
needs live game knowledge, the domain layer should expose delegates or context
objects and let the product layer provide the current card pool, role owner
hint, resource path, authority state, or UI selection.

## Ownership And Mutability

Every registered artifact must have a stable `ownerModId`. Ownership controls
who may edit source data; it is not merely a display label.

Registered artifacts from another Mod are read-only to product tools. They may
be selected, inspected, referenced, or copied. They must not be edited or
deleted by a non-owner tool.

Owner-local artifacts are editable by their owner. For example, AuraToolsExp may
edit AuraToolsExp-local StarterDeck profiles, but it must not mutate
SunExp-owned registered StarterDeck profiles.

When a read-only registered artifact is copied into editable user/tool config,
the copy becomes a new owner-local artifact. Preserve a source pointer such as
`derivedFromProfileId` when useful, but future edits belong to the local copy.

## Conflict And Candidate Policy

Do not treat "two artifacts can apply to the same target" as a technical
conflict. They are usually separate candidates that should be listed and
resolved through priority or explicit user selection.

Technical conflict is based on identity:

- Core resource registry: `system::resourceId`.
- Package installs: `system::logicalId`.
- Domain registries: the domain-defined qualified identity, normally
  `ownerModId + ":" + localId`.

If a component uses a globally unique provider id instead of an owner-qualified
identity, that rule must be documented and provider ids should be prefixed by
the owning Mod. Otherwise, duplicate ids from different Mods can accidentally
replace each other.

## Resolution Priority

For user-selectable content, use this default resolution order unless the
domain has a stronger reason to differ:

1. explicit user selection;
2. registered artifact owned by the target role/content owner;
3. owner-local role-specific user/tool artifact;
4. owner-local global user/tool artifact;
5. non-owner registered fallback, only when the caller explicitly enables it;
6. built-in/default game behavior.

Invalid explicit selections should remain visible to UI so the user can clear
or repair them. Runtime resolution may skip them and continue through fallback.

For provider-driven domains such as audio, BGM, and CG, the equivalent rule is:

- explicit request or direct provider id wins when supported;
- higher priority is tried before lower priority;
- `hardClaim` or equivalent ownership claims should stop fallback only when the
  domain has documented what happens if the claimed resource is missing;
- ties must have deterministic ordering.

## Compatibility

Any shared component that creates a persistent global `GameObject` component
must expose:

- `CurrentProtocolVersion`;
- `MinimumSupportedProtocolVersion`;
- a build id when public method shape matters;
- a reflected method-shape compatibility check before reuse.

If compatibility fails, disable that component for the current consumer and log
the reason. Do not crash unrelated gameplay initialization.

## Multiplayer And Authority

Shared components that advance state must identify the authority writer. Clients
may update presentation, but shared progression or shared runtime state should
be committed only by the authoritative side.

State sync messages should carry enough identity and content hash/version data
for receivers to validate local resources and fall back cleanly.

## Development Checklist

When adding a shared component or a new registered artifact type, check:

- Is this Core service, domain shared component, product/tool UI, or utility?
- Does the contract define `ownerModId` and a stable qualified identity?
- Are registered artifacts immutable to non-owner editors?
- Are local editable artifacts stored under the correct owner?
- Are same-target candidates listed/resolved instead of silently deduplicated?
- Is the priority/fallback order centralized in the shared domain layer?
- Does validation return machine-readable status or reasons?
- Do writes go through Core storage, registry, or package services?
- Does the global runtime have protocol/build/method compatibility checks?
- Are multiplayer state changes authority-gated?
- Is the contract documented with a manifest example or integration checklist?

