# AuraSkinShared

`AuraSkinShared` is the shared character-skin installer and runtime used by SkinExp, AuraToolsExp, and SunExp.

## Runtime model

- Every consumer compiles the same shared source into its own DLL.
- The first consumer creates the persistent `AuraSkin.Global` component; later consumers use its reflected protocol.
- Mod resources under `SharedResources/Skins` are installation sources only.
- The runtime enumerates only skin packages registered in the current session.
- Canonical payloads use `Skin/Role/<careerId>/Skin/<owner>/<skinId>/content`;
  registered v2 `Skins/<careerId>/<skinId>` paths remain readable as aliases.

## Persistent data

- Installed skins: `ModsData/AuraShared/Skin/Role/<careerId>/Skin/<owner>/<skinId>/content`
- Owner package registry: `ModsData/AuraShared/_Registry/V3/Owners/<owner>/<packageId>.json`
- Active leases: `ModsData/AuraShared/_Runtime/Leases/<session>/<owner>/<packageId>.json`
- Selections: `ModsData/AuraShared/Config/Shared/Skin/selections.json`
- Staging: `ModsData/AuraShared/Cache/Packages`
- Replaced payload backups: `ModsData/AuraShared/Backups/Skin`
- Transaction journals: `ModsData/AuraShared/Transactions`

Publication source folders may use shorter names because the installer derives canonical scope and resource identities from the manifests.

## Installation and deduplication

Consumers call `AuraSkinRuntime.RegisterPackage(modConfig, ownerModId)`. SkinShared validates skin manifests, translates
the package into v3 resource declarations, and delegates path policy, migration aliases, storage, hashing, leases, locking,
and recovery to AuraSharedCore. Registration identity is `(ownerModId, packageId)` and resource identity is the normalized
`targetCareerId::skinId` pair. Multiple packages from one owner remain active together, while repeated registration of one
package is idempotent.
