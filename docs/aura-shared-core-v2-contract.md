# AuraSharedCore v2 Contract

AuraSharedCore v2 is the shared runtime protocol used by participating Mod DLLs.
The first compatible consumer creates `AuraShared.Global`; later consumers call
that global component through a reflected JSON protocol. Core owns shared writes,
resource installation, revision checks, package transaction recovery, operation
logs, and deterministic snapshots. System adapters own business parsing and
runtime behavior.

## Compatibility

- `CurrentProtocolVersion`: 2
- `MinimumSupportedProtocolVersion`: 2
- `BuildId`: `aura-shared-core-2026-06-22-v2`

A consumer must refuse an existing global component when protocol, minimum
version, BuildId, or public method shape differs. The refusal should disable
shared services for that consumer and log the reason; it must not crash unrelated
gameplay initialization.

## Stable Component Methods

The stable reflected method set is:

- `InitializeOwner(modConfig, ownerModId, options)`
- `ReadStorageJson(requestJson)`
- `WriteStorageJson(requestJson)`
- `InstallResourceJson(requestJson)`
- `GetInstalledResourcesJson(system)`
- `GetChangesJson(sinceSequence)`
- `RegisterManifestPath(ownerModId, manifestPath, baseDirectory)`
- `RegisterManifestJson(ownerModId, manifestJson, baseDirectory)`
- `GetResourcesJson(system)`
- `GetOwners()`

Requests and responses are serialized JSON. Callers should use the typed wrappers
in `AuraSharedStorage`, `AuraSharedConfigStore`, `AuraSharedPackageEngine`, and
`AuraSharedRegistry` instead of invoking the component directly.

## Storage Request Template

```json
{
  "scope": "Owner",
  "system": "AuraTools",
  "ownerModId": "AuraToolsExp",
  "writerId": "AuraToolsExp",
  "authorityId": "AuraToolsExp",
  "fileName": "AudioSettings.json",
  "schemaVersion": 1,
  "expectedRevision": 3,
  "payloadJson": "{\"enabled\":true}",
  "createBackup": true
}
```

Rules:

- `Shared` documents have one authority writer.
- `Owner` documents can only be written by their owner.
- `Runtime` documents are rebuildable state and are not user configuration.
- Writes with a non-negative `expectedRevision` must match the current revision.
- Writes are locked by document key and protected by a cross-process mutex.
- JSON is written through a flushed temp file and atomic replacement or rollback.

## Package Install Request Template

```json
{
  "ownerModId": "SunExp",
  "system": "Audio",
  "logicalId": "SunExp.WuNa.VoicePack",
  "packageId": "SunExp.SharedResources",
  "packageVersion": 1,
  "kind": "Directory",
  "sourcePath": "D:/.../SunExp/SharedResources/Audio/WuNa",
  "destinationRelativePath": "Audio/SunExp/WuNa"
}
```

Rules:

- Identity is `system::logicalId`.
- Equal content hash merges `sources`.
- Same owner can replace content only with a higher package version.
- Different owners cannot implicitly replace different content.
- Installation is locked by resource key, then registry key, then write mutex.
- Core records a short-lived transaction journal and an append-only operation log.

## Adapter Manifest Shape

System adapters should translate their business manifest into Core requests:

```json
{
  "system": "Audio",
  "adapterVersion": 1,
  "ownerModId": "SunExp",
  "capabilities": ["PackageInstall", "RuntimeResolve"],
  "resources": [
    {
      "logicalId": "SunExp.WuNa.VoicePack",
      "kind": "Directory",
      "source": "Audio/WuNa",
      "destination": "Audio/SunExp/WuNa"
    }
  ]
}
```

Adapters may understand Skin, Audio, CG, Log, or Journey semantics. Core must not.

Domain-specific shared arbiters, such as `StarterDeckArbiterShared`, own their
own business contracts on top of Core. Core provides registry, storage, locking,
and diagnostics; it must not decide StarterDeck profile priority, editability, or
role ownership. See `docs/starter-deck-profile-protocol.md` for the StarterDeck
domain contract, and `docs/shared-component-architecture-guidelines.md` for the
general shared-component rules.

## Operation Log

Operation logs are append-only JSONL files under:

```text
Logs/Operations/yyyyMMdd.jsonl
```

Each record uses:

```json
{
  "timestampUtc": "2026-06-22T10:00:00Z",
  "operationId": "op",
  "transactionId": "tx",
  "ownerModId": "SunExp",
  "system": "Audio",
  "logicalId": "SunExp.WuNa.VoicePack",
  "kind": "InstallResource",
  "phase": "RegistryCommitted",
  "result": "Success",
  "revision": 0,
  "message": "Registry committed.",
  "elapsedMs": 18
}
```

`Transactions/<id>.json` remains the recovery source of truth. Operation logs are
for diagnostics and automated assertions; failure to write them must never change
runtime behavior.

## Lock Keys

- Config document: `Config/<Scope>/<Owner>/<System>/<File>`
- Resource install: `Resource/<System>/<LogicalId>`
- Registry: `Registry/<System>`
- Recovery: `Transactions/Recovery`

All resource installs must acquire locks in this order:

```text
Resource -> Registry -> cross-process write mutex
```

## Release Gate

The release gate is `tools/Test-SharedReleaseGate.ps1`. It loads
`tools/shared-release-matrix.json`, runs enabled steps, and fails the build when a
contract, scan, adapter, package, or consumer-build check fails.

The architecture guideline scan is `tools/Test-SharedArchitectureGuidelines.ps1`.
It enforces global-runtime compatibility surfaces, provider identity rules,
shared-write boundaries, and required shared-component documentation anchors.
