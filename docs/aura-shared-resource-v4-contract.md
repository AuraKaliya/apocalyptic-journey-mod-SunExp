# AuraShared resource protocol v4

AuraShared v4 is a breaking, registration-only protocol. Runtime protocol 5 is
required; v3 manifests, legacy paths, unregistered files, and earlier user
configuration are not imported or resolved.

## Canonical hierarchy

Every resource, including a user-created resource, uses this hierarchy from the
`AuraShared` root:

`moduleId/scopeType/canonicalScopeId/featureId/ownerModId/resourceId/content`

Each level owns a JSON document (`aura.shared.json`, `aura.module.json`,
`aura.scope-type.json`, `aura.scope.json`, `aura.feature.json`,
`aura.provider.json`, `aura.resource.json`). Configuration is read on demand.
Redundant documents and payloads may remain when a MOD is inactive.

## Required registration

Package manifests use `schemaVersion: 4`. Each declaration explicitly provides
the canonical scope, `scopeOwnerModId`, optional `scopeAliases`, `originKind`,
`writerId`, and `defaultEnabled`. Package registration is atomic: an invalid,
conflicting, or unavailable declaration prevents activation of the package.

Valid origins are `ContentRegistered`, `ToolRegistered`, `ToolDefault`,
`FoundationDefault`, and `UserManual`. Package resources are read-only seeds.
Editing a package resource creates a separate manual resource rather than
changing ownership.

User manual resources are owned and registered by the managing tool MOD, use
`packageSourceKind: LocalPackage`, and always have `writerId: LocalUser`. They
are stored in the same canonical hierarchy as package resources.

## Activity and history

Persistent registration and current-session activity are separate. The normal
catalog view contains only registered, active, available, applicable, and
non-archived resources. The independent History view contains resources with
one or more reasons: `InactiveOwner`, `Unavailable`, `Archived`, `Retired`,
`Inapplicable`, or `Invalid`. A user-disabled resource remains in the normal
view as an unchecked resource; it is not history.

Old raw files and v3 configuration are ignored, not listed as history.

## Configuration and selection

`aura.user.json` is sparse and contains `schemaVersion`, `writerId`, `enabled`,
`selectionMode`, `resourceOverrides`, and domain values. Supported shared
selection modes are `Priority`, `Random`, `Sequential`, and `All`. Domains may
advertise a subset, but multiple enabled resources always enter this common
selection pipeline.

Catalog state distinguishes `registered`, `active`, `applicable`, `available`,
`configuredEnabled`, and `effectiveEnabled`. Full IDs are canonical. Short IDs
are aliases only and are matched through the scope's active domain identity
snapshot, such as the active role registry.
