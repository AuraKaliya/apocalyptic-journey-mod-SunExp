# Learnings

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
