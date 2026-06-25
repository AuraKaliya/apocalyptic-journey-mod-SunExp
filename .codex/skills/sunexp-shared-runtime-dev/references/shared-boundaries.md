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

## Tests

Keep architecture tests close to the contract. Shared tests currently scan for
raw shared writes, required documentation anchors, authority checks, and known
consumer contracts.
