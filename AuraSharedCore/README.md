# AuraSharedCore v2

AuraSharedCore is the common storage and coordination layer compiled into every participating Mod DLL. The first loaded
consumer owns the persistent `AuraShared.Global` component; later consumers call it through the reflected protocol.

## Storage model

- Shared configuration: `Config/Shared/<System>`
- Owner-only configuration: `Config/Owners/<Owner>/<System>`
- Rebuildable runtime state: `Config/Runtime/<System>`
- Resource indexes: `Registries/<System>/resources.json`
- Package staging: `Cache/Packages`
- Transaction recovery: `Transactions`
- Replaced payload backups: `Backups/<System>`
- Owner-only logs: `Logs/<Owner>`

Configuration documents are revisioned envelopes. Shared documents have one authority writer; owner documents can only be
written by their owner. Reads use immutable snapshots, writes use an in-process reader/writer lock plus a cross-process
named mutex, and JSON replacement is flushed before atomic replacement.
After a write lock is released, the global component appends a revisioned change record. Cross-DLL consumers poll
`AuraSharedStorage.GetChanges` instead of attempting to share CLR event delegates between separately compiled assemblies.

## Resource model

`AuraSharedPackageEngine` installs files or directories under a canonical `system::logicalId` identity. Equal hashes merge
sources, same-owner updates require a higher package version, and different cross-owner content is rejected. Each content
commit uses staging, a persistent transaction journal, a resource-index update, and deterministic rollback or startup recovery.

System modules parse their own manifests and produce generic install requests. Core does not understand skin, audio, or CG
semantics. Logs remain owner-written and are exposed for shared aggregation through `AuraSharedLogStore`.
