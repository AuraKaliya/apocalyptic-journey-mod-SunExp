# Aura shared resource identity audit

Audit date: 2026-07-20.

The shared runtime uses one rule across domains:

```text
preserve registrations -> validate qualified identity -> group semantic candidates
-> apply explicit domain policy -> deduplicate physical content by hash only
```

| Module | Registration identity | Candidate/conflict policy | Audit result |
| --- | --- | --- | --- |
| AuraSharedCore v3 catalog | Full module/scope/feature/owner/resource tuple | Cross-owner semantic duplicates coexist; same-owner active package collision is invalid | Updated |
| AuraSkinShared | `ownerModId:targetCareerId:skinId` | All candidates retained; AuraTools local list gates candidates; selections and sync use qualified ids | Updated from destructive semantic deduplication |
| AuraCgShared | `ownerModId:cgId` | Cross-owner entries coexist; enabled candidates use shared priority/random/sequential selector; duplicate qualified ids in one or multiple owner contributions are rejected | Verified and hardened |
| AuraCardUseFxShared | `ownerModId:effectId` | Qualified entries coexist; stack/exclusive policy resolves semantic overlap; duplicate qualified manifest entries are rejected | Verified and hardened |
| AudioArbiterShared | `ownerModId:providerId` | Resolver carries owner for strict/remote requests and uses priority for intentionally unscoped requests | Verified |
| BattleBgmArbiterShared | `ownerModId:providerId` | Priority resolves normal arbitration; explicit switch accepts a bare id only when unique and rejects ambiguity | Updated |
| StarterDeckArbiterShared | `ownerModId:profileId` | All profiles retained; explicit selection, role ownership and priority form deterministic resolution | Verified |
| AuraJourneyShared | owner-qualified journey id | Definitions and state use the same qualified journey id | Verified |
| AuraModeShared | owner-qualified mode id | Definition and state reads are owner-scoped | Verified |
| AuraRoleShared | qualified contribution id plus canonical role id | Contributions are preserved; repeated role semantics merge aliases/tags by explicit priority instead of being discarded during normalization | Updated |
| AuraDirectorShared | `ownerModId:sourceId` | Sources coexist and are compiled in priority/owner order | Verified |
| AuraUiShared | caller-defined style key | Built-ins are global; consumer styles already use owner-prefixed keys | Verified; API remains intentionally global |
| AuraOnlineShared chat catalog | signed catalog id/hash and catalog-local content ids | One authoritative encrypted catalog is loaded at a time; duplicate ids are rejected during validation | Not a multi-provider resource registry |

Cache keys, replay ids, hook ids, player ids, and animation-state names were excluded
when they represented lifecycle deduplication rather than cross-MOD resource identity.
