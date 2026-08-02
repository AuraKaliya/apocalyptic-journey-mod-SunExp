# AuraSharedCore v2

AuraSharedCore is the common storage and coordination layer compiled into every participating Mod DLL. The first loaded
consumer owns the persistent `AuraShared.Global` component; later consumers call it through the reflected protocol.

## Storage model

- Shared configuration: `Config/Shared/<System>`
- Owner-only configuration: `Config/Owners/<Owner>/<System>`
- Rebuildable runtime state: `Config/Runtime/<System>`
- Owner-only durable data: `Data/Owners/<Owner>/<System>`
- Resource indexes: `Registries/<System>/resources.json`
- Package staging: `Cache/Packages`
- Transaction recovery: `Transactions`
- Replaced configuration backups: `Backups/Storage/Versions`
- Owner-only logs: `Logs/<Owner>/<System>`

Package defaults remain inside each Mod package. User-editable settings belong under `Config/Owners`; durable profiles,
model libraries, and similar non-log payloads belong under `Data/Owners`; diagnostics and generated reports belong under
`Logs`. Callers should use `AuraSharedPaths` instead of rebuilding these paths independently.

Configuration documents are revisioned envelopes. Shared documents have one authority writer; owner documents can only be
written by their owner. Reads use immutable snapshots, writes use an in-process reader/writer lock plus a cross-process
named mutex, and JSON replacement is flushed before atomic replacement.
Semantically identical JSON is a no-op: it does not advance the revision or create a backup, operation-log entry, or
change-feed entry. Raw atomic text writes likewise skip byte-identical replacements. Changed configuration documents retain
at most 12 version backups per logical path.
After a changed write lock is released, the global component appends a revisioned change record. Cross-DLL consumers poll
`AuraSharedStorage.GetChanges` instead of attempting to share CLR event delegates between separately compiled assemblies.

## Resource model

`AuraSharedPackageEngine` installs files or directories under a canonical `system::logicalId` identity. Equal hashes merge
sources, same-owner updates require a higher package version, and different cross-owner content is rejected. Each content
commit uses staging, a persistent transaction journal, a resource-index update, and deterministic rollback or startup recovery.

System modules parse their own manifests and produce generic install requests. Core does not understand skin, audio, or CG
semantics. Logs remain owner-written and are exposed for shared aggregation through `AuraSharedLogStore`.

`AuraSharedResourceBootstrapper.Bootstrap` is the common startup entry point for bundled file and directory packages.
Each Mod invokes it with its own `ModConfig`, owner id, and manifest path. The result reports installed, repaired, updated,
deduplicated, conflicting, and failed resources. Runtime systems register or reload their providers only after bootstrap;
the bootstrapper does not scan or install resources on behalf of other Mods.

## Package manifest contract

`SharedResources/aura.registration.json` is the required v4 entry point for bundled shared resources in main Mods. The main Mods are
`Terrias`, `SanGuoShaExp`, and `AuraToolsExp`; prototype packages under `TestMods` may exercise the same APIs but must not
define the production contract.

The schema is stored at `AuraSharedCore/Schemas/resource-package.schema.json`. Version 1 keeps `resources` backward
compatible and adds optional platform metadata:

- `ownerModId`: the Mod that owns and installs the package. When present it must match the installing Mod.
- `packageKind`: `Resource`, `RolePack`, `JourneyPack`, `AudioPack`, `CgPack`, `SkinPack`, or `ToolingPack`.
- `capabilities`: declares the systems the package expects, such as `Audio`, `Skin`, `Journey`, `RolePack`, or
  `MultiplayerAuthority`.
- `dependencies`: package-level dependencies with optional minimum versions.
- Resource `targetRoleIds`, `tags`, and `metadata`: lightweight indexing fields for complete role packs and tooling.

The package engine still installs only files and directories. Higher-level systems such as skins, audio, CG, and journeys
translate their manifests into generic install or storage requests.

## Diagnostics contract

Shared services should log through `AuraSharedDiagnostics` when a step crosses service boundaries, writes shared state, or
depends on multiplayer authority. Each record includes service, owner Mod, phase, level, optional authority flag, and an
optional correlation id such as a journey id or resource id. Owner-specific file logs are still written through
`AuraLogShared`; diagnostics are the common message shape used by shared services and command-log mirrors.
