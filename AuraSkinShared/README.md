# AuraSkinShared

`AuraSkinShared` is the shared character-skin installer and runtime used by SkinExp, AuraToolsExp, and SunExp.

## Runtime model

- Every consumer compiles the same shared source into its own DLL.
- The first consumer creates the persistent `AuraSkin.Global` component; later consumers use its reflected protocol.
- Mod resources under `SharedResources/Skins` are installation sources only.
- The runtime enumerates only skin packages registered in the current session.
- Logical skin paths use `Skin/Role/<careerId>/Skin/<owner>/<skinId>/content`.
  Long identities are stored physically under `Skin/_Store/<prefix>/<hash>/content`
  and remain resolvable through the logical path;
  registered v2 `Skins/<careerId>/<skinId>` paths remain readable as aliases.

## Persistent data

- Installed skins: the readable logical hierarchy above, or the compact
  `ModsData/AuraShared/Skin/_Store/<prefix>/<hash>/content` hierarchy when the
  logical resource directory exceeds the portable path budget
- Owner package registry: `ModsData/AuraShared/_Registry/V4/Owners/<owner>/<packageId>.json`
- Active leases: `ModsData/AuraShared/_Runtime/Leases/<session>/<owner>/<packageId>.json`
- Selections: `ModsData/AuraShared/Config/Shared/Skin/selections.json`
- Staging: `ModsData/AuraShared/Cache/Packages`
- Replaced payload backups: `ModsData/AuraShared/Backups/Skin`
- Transaction journals: `ModsData/AuraShared/Transactions`

Publication source folders may use shorter names because the installer derives canonical scope and resource identities from the manifests.

## Installation, identity, and candidate selection

Consumers call `AuraSkinRuntime.RegisterPackage(modConfig, ownerModId)`. SkinShared validates skin manifests, translates
the package into v4 resource declarations, and delegates path policy, migration aliases, storage, hashing, leases, locking,
and recovery to AuraSharedCore. Registration identity is `(ownerModId, packageId)` and resource identity is the normalized
`Skin/Role/targetCareerId/Skin/ownerModId/skinId` tuple. `targetCareerId::skinId` is only the semantic grouping key.

Different owners may publish the same role and skin id. `SkinRegistry` keeps every
`ownerModId:targetCareerId:skinId` candidate, groups
semantic duplicates, and orders them by priority and qualified id. It never uses directory order as conflict policy.
Selections and multiplayer snapshots persist the qualified id; legacy bare ids are resolved deterministically and rewritten
on the next explicit selection. AuraToolsExp can enable or disable individual qualified candidates while leaving inactive
or temporarily missing MOD ids in JSON. With no tool override, all content-owned candidates remain enabled.

Repeated registration of one package is idempotent. A second active package from the same owner cannot claim an existing
qualified resource identity. Equal content hashes may share physical package storage, but their owner registrations remain
independent.

Registration always refreshes the skin catalog after a package is successfully
activated, including a deduplicated package on a new process session. Failures
log expected and processed resource counts plus the structured Core failure,
path, and path length instead of reporting an unexplained rejection.
