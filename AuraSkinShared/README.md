# AuraSkinShared

`AuraSkinShared` is the shared character-skin installer and runtime used by SkinExp, AuraToolsExp, and SunExp.

## Runtime model

- Every consumer compiles the same shared source into its own DLL.
- The first consumer creates the persistent `AuraSkin.Global` component; later consumers use its reflected protocol.
- Mod resources under `SharedResources/Skins` are installation sources only.
- The runtime scans only `ModsData/AuraShared/Skins`.

## Persistent data

- Installed skins: `ModsData/AuraShared/Skins`
- Install registry: `ModsData/AuraShared/Registries/Skin/resources.json`
- Selections: `ModsData/AuraShared/Config/Shared/Skin/selections.json`
- Staging: `ModsData/AuraShared/Cache/Packages`
- Replaced payload backups: `ModsData/AuraShared/Backups/Skin`
- Transaction journals: `ModsData/AuraShared/Transactions`

Installed character and skin directory names are canonical: they equal `targetCareerId` and `skinId` respectively.
Publication source folders may use shorter names because the installer derives the destination from the manifests.

## Installation and deduplication

Consumers call `AuraSkinRuntime.RegisterPackage(modConfig, ownerModId)`. SkinShared validates skin manifests and delegates
all storage, hashing, ownership, version arbitration, locking, and recovery to `AuraSharedPackageEngine`. Identity is the normalized
`targetCareerId::skinId` pair. Equal identities and SHA-256 content hashes are deduplicated. Different content can only
replace a resource owned solely by the same Mod and requires a higher integer `packageVersion`; cross-owner conflicts are
rejected. Payloads are copied through staging and committed with rollback backups.
