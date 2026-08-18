# Aura Tooling Shared v1 Contract

## Purpose

`AuraTooling.Shared` is the semantic-free extension protocol that lets an
independent tool MOD publish one of its own tools into a compatible toolbox
surface such as AuraToolsExp.

The protocol does not make AuraToolsExp a dependency of the provider. Both the
provider and AuraToolsExp depend on `Aura.Shared.dll`; AuraToolsExp projects the
shared registration into its local module UI.

Content resources such as skins, CGs, and audio must continue to
use their existing domain registries. They are entries inside an existing tool
page, not new top-level tools.

## Identity And Ownership

Every extension uses the stable identity:

```text
ownerModId:moduleId
```

- `ownerModId` must be the registering MOD's stable owner ID.
- `moduleId` must be stable within that owner.
- Both IDs may contain letters, digits, `.`, `_` and `-` only.
- Display text, localization and load order must not participate in identity.
- Registering a different provider under an existing identity is rejected.
- Registering the same provider again is idempotent.

## Registration

```csharp
var result = AuraToolExtensionRegistry.Register(
    "ExampleTools",
    new ExampleToolProvider());
if (!result.Success)
{
    // Log result.Message through the provider MOD's logger.
}

// Keep the handle for the provider lifetime.
IDisposable? registration = result.Handle;
```

Every successful registration call returns one lease. Repeated registration by
the same provider is idempotent but still returns a lease; the extension is
removed after all of its leases are disposed. Late registration and final
unregistration increment the registry revision and notify consumers. Consumers
must refresh from a new snapshot instead of assuming one global "all mods
loaded" phase.

## Provider Contract

An `IAuraToolExtensionProvider` owns four operations:

- `Descriptor`: immutable user-facing metadata and protocol identity.
- `SnapshotState()`: current configured/effective state and compact status.
- `SetEnabled(bool)`: the provider-owned master switch.
- `ShowSettings(Transform)`: an optional provider-owned settings page.

The provider remains responsible for initialization, persistence, validation,
hooks, cleanup and multiplayer authority. Registration does not transfer those
responsibilities to AuraToolsExp.

When state changes independently of a toolbox action, the registered provider
may call `AuraToolExtensionRegistry.NotifyStateChanged` with its owner, module,
provider instance and monotonically increasing state revision. Notifications
from an unregistered or different provider are rejected. AuraToolsExp refreshes
only the matching projected row.

## Descriptor Rules

- `ProtocolVersion` and `MinimumSupportedProtocolVersion` must overlap the
  consumer's supported range. A newer provider may remain usable when it
  explicitly preserves the current baseline.
- `DisplayName` is required and limited to 96 characters.
- `Description` is limited to 240 characters and should explain user value.
- At most 16 distinct search terms are retained.
- `Order` is clamped to `0..10000`.
- Unknown category IDs appear under the `extensions` category.
- `HasSettingsPage` must be false when the provider has no meaningful page.
- `Experimental` and `RequiresRestartWhenChanged` are presentation metadata;
  the provider still owns the actual safety and restart behavior.

## State And Failure Isolation

The state snapshot distinguishes configured enablement from effective
enablement and reports one of:

```text
Ready, Disabled, Unavailable, Degraded, Busy, RestartRequired
```

AuraToolsExp catches provider failures while reading state, changing the master
switch or opening settings. A faulty provider is shown as degraded and must not
break built-in tools or registry ownership.

Registry change notifications are also isolated. One faulty consumer callback
cannot cancel registration or prevent other consumers from observing the new
revision.

## Settings UI

The provider owns all feature-specific controls and receives a parent
`Transform` only when the user opens its settings. Providers should use shared
Aura UI primitives and normal overlay safety rules, but they must not access
AuraToolsExp private configuration, module instances or UI trees.

Closing or removing the provider must release provider-owned UI, subscriptions
and temporary objects. AuraToolsExp owns only the projection row and routing
adapter.

## Persistence And Networking

The registry is process-local and does not persist provider state. Providers
must use their own owner-qualified storage.

The protocol does not grant multiplayer authority. Any shared state, RPC,
payload validation, duplicate suppression or sender binding remains the
provider's responsibility through the appropriate shared networking protocol.

## Compatibility

Version 1 currently exposes the range `1..1`. Providers declare both their
current and minimum supported versions, so a future additive protocol may keep
the v1 interface available without being rejected only because its current
version increased. Breaking changes still require a new protocol version and a
non-overlapping minimum; they must not reinterpret an existing descriptor or
provider method in place.
