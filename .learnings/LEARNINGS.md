# Learnings

## [LRN-20260728-003] knowledge_gap

**Logged**: 2026-07-28T18:35:00+08:00
**Priority**: high
**Status**: resolved
**Area**: backend

### Summary
Final-boss validation level and enemy IDs must come from the bundled runtime Level export, not names or remembered mappings.

### Details
The authoritative mappings are `level_0 -> enemy_10027`, `level_10046 -> enemy_10048` (with sword adds), `level_10048 -> enemy_10055`, and `level_10051 -> enemy_10058`. Similar level and enemy numbers are not interchangeable.

### Suggested Action
Keep the release test that checks the hidden game-host validation cases against `combat-knowledge.base-game.json`.

### Metadata
- Source: error
- Related Files: AuraToolsExp/Config/combat-knowledge.base-game.json, AuraToolsAutoBattleGameValidationRuntime.cs
- Tags: auto-battle, boss, authority, validation

---

## [LRN-20260716-001] correction

**Logged**: 2026-07-16T14:40:00+08:00
**Priority**: medium
**Status**: pending
**Area**: infra

### Summary
Archived TestMods prototypes are not mandatory shared-release consumers after their features have been integrated into main mods.

### Details
The shared DLL hash gate currently treats active product mods and retired prototype mods identically. Prototype packages are normally disabled after integration and should not force routine release rebuilds unless their own compatibility is being tested.

### Suggested Action
Split shared packaging validation into required active consumers and optional prototype compatibility consumers; run the optional set only explicitly or when prototype sources are changed.

### Metadata
- Source: user_feedback
- Related Files: tools/Test-SharedDllPackaging.ps1, tools/shared-release-matrix.json
- Tags: release-gate, prototypes, shared-runtime

---

## [LRN-20260716-002] correction

**Logged**: 2026-07-16T18:20:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: backend

### Summary
Endless Abyss active evacuation is intentionally available on every floor and counts as a successful mode clear.

### Details
Do not impose the earlier proposed floor-seven gate. Once the opening theme deck is committed and the run reaches stable map planning, evacuation may settle even at floor one with zero completed nodes. Multiplayer remains host-authoritative and the first release has no client request or vote flow.

### Suggested Action
Keep eligibility tied to stable lifecycle state and unresolved pressure/reward gates, not floor number or completed-node count.

### Metadata
- Source: user_feedback
- Related Files: Terrias-Dev/Hooks/EndlessAbyssEvacuationRuntime.cs, Terrias-Dev/Mechanics/EndlessAbyssEvacuationDepth.cs
- Tags: endless-abyss, evacuation, multiplayer, settlement

---
