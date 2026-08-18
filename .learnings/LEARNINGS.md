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

## [LRN-20260729-001] correction

**Logged**: 2026-07-29T10:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: docs

### Summary
Base-game combat guidance must distinguish deck reshuffle cycling, the `Recycle` card keyword, and a resource-closed infinite loop.

### Details
Player block persists between turns. When a draw request exceeds the remaining
draw pile, the discard pile is shuffled back and drawing continues. Unretained
cards normally enter the discard pile at turn end, while retained or
`Recycle`-keyword cards occupy hand slots and can reduce future draw throughput.
`ritualcard_8` converts all damage dealt after activation into persistent block
at turn end, making damage loops a major defensive engine against
damage-limiting bosses.

### Suggested Action
Correct the combat-flow document, use separate terms for deck cycling,
`Recycle`-keyword repetition, and infinite resource closure, then add regression
coverage for persistent block, mid-draw reshuffling, and Ritual Courage block
conversion.

### Metadata
- Source: user_feedback
- Related Files: docs/游戏主体内容/战斗流程/游戏主体战斗流程与高价值构筑清单.md, AuraCombatSimulationShared/CombatSimulationEngine.cs, AuraCombatAiShared/CombatLoopSafetyAnalyzer.cs
- Tags: combat-flow, deck-cycle, retain, shield, ritual

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

## [LRN-20260803-001] correction

**Logged**: 2026-08-03T00:00:00+08:00
**Priority**: critical
**Status**: resolved
**Area**: backend

### Summary
Nana training conclusions must be based on exact base-game MaxHp, DoomPower,
and transformation semantics, and the current `journey-final-max-hp` metric is
not a true terminal-battle metric after replay sampling.

### Details
The base game heals current HP by the positive MaxHp delta in
`StatusManager.set_MaxHp`. `DoomPower` persists through `PlayerInfo.SpecialVars`
for the whole adventure. Calamity form deals `Self.MaxHp / 50` to all enemies
on every action and changing from `career_2` to `career_4` does not itself
modify MaxHp. The simulator currently omits the MaxHp-gain heal and applies a
synthetic 20-MaxHp form delta. In addition, role diagnostics choose the highest
battle index remaining in the sampled replay, which may not be the journey's
actual terminal battle.

### Suggested Action
Repair semantic parity before retraining, persist exact terminal campaign
MaxHp and DoomPower before replay sampling, invalidate incompatible training
artifacts, and add focused lifecycle tests.

### Metadata
- Source: user_feedback
- Related Files: AuraToolsExp-Dev/Features/AutoBattle/AuraToolsNativeRewardSimulationRuntime.cs, AuraToolsExp-Dev/Features/AutoBattle/AuraToolsAuthoritativeRoleSemantics.cs, AuraToolsExp-Dev/Features/AutoBattle/AuraToolsNanaRoleStrategy.cs, AuraToolsExp-Dev/Features/AutoBattle/AuraToolsRoleTrainingDiagnostics.cs
- Tags: nana, doom-power, max-hp, transform, training-metrics

### Resolution
- **Resolved**: 2026-08-03T00:00:00+08:00
- **Notes**: Implemented positive MaxHp-delta healing, removed synthetic form
  MaxHp changes, preserved DoomPower across battles, modeled the 2%-MaxHp
  all-enemy action trigger, and sourced terminal metrics from complete training
  campaign observations before replay cleanup.

---

## [LRN-20260818-001] correction

**Logged**: 2026-08-18T11:10:00+08:00
**Priority**: high
**Status**: resolved
**Area**: frontend

### Summary
An independent UI preview must reproduce the game's actual visual grammar, not only its information architecture.

### Details
The first Unity settings preview used a modern flat charcoal workspace, small
type, pill switches, and thin borders. The real game settings UI uses saturated
indigo surfaces, warm large text, ornate gold nine-slice frames, paired
enable/disable checkboxes, and long selectors with gold end caps. A structurally
correct preview was therefore visually misleading.

### Suggested Action
Calibrate against supplied game screenshots and extracted native nine-slice
assets before accepting preview fidelity. Keep visual-structure assertions in
the Player suite so later theme simplification cannot silently drift back to a
generic tool UI.

### Metadata
- Source: user_feedback
- Related Files: AuraToolsUnityUiPreview, AuraToolsExp/ui-img, Terrias/ModResource/Images/UI
- Tags: unity-preview, ui-fidelity, native-style, nine-slice, settings

### Resolution
- **Resolved**: 2026-08-18T11:45:00+08:00
- **Notes**: Recalibrated the Player from the supplied screenshot, copied and
  sliced the native button/panel/selector assets, restored indigo surfaces,
  large warm text, paired checkboxes and gold-capped selectors, and added
  runtime assertions for the native visual components.

---

## [LRN-20260818-002] correction

**Logged**: 2026-08-18T12:20:00+08:00
**Priority**: high
**Status**: resolved
**Area**: frontend

### Summary
Native visual fidelity requires selective adaptation; repeating ornate native assets across every toolbox control still produces an ugly interface.

### Details
The second Unity preview used the correct indigo palette and original nine-slice
assets, but reused the same small native button and panel textures for tabs,
category selection, header, search, icon buttons, selectors and checkboxes.
The result matched the game's material vocabulary while losing visual hierarchy
through repeated gold frames, non-integer scaling and very dense text.

### Suggested Action
Keep native chrome only at the settings-window boundary and native setting
controls. Create a restrained Aura Toolbox component family for internal
navigation, search, module rows, icon actions and checkboxes, with per-resource
filtering and a shared visual specification used by production and previews.

### Metadata
- Source: user_feedback
- Related Files: docs/AuraToolsExp/toolbox-ui-component-redesign-plan.md, AuraToolsUnityUiPreview
- Tags: ui-hierarchy, resource-semantics, gold-frame-noise, native-adaptation

### Resolution
- **Resolved**: 2026-08-18T14:10:00+08:00
- **Notes**: Added a dedicated Toolbox V2 asset family and visual spec, removed
  ornate native controls from the toolbox interior, increased density metrics,
  labeled module enable actions, and synchronized production, HTML and Unity
  previews with source and screenshot gates.

---
