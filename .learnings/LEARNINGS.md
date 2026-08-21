# Learnings

## [LRN-20260820-001] correction

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: medium
**Status**: pending
**Area**: docs

### Summary
Terrias瞬间文案应依靠未完成的动作和具体物件收束，避免用人物顿悟或总结性金句替读者解释主题。

### Details
用户指出“她没有回答，只……”以及“直到……她才第一次明白：……”具有明显AI感。问题来自模板化转折、为道具强加象征功能、显式宣布人物领悟，以及用工整比喻总结已经能够由场景自行表达的含义。

### Suggested Action
改写此类短篇时，删除解释性认知句和结论冒号，让人物继续做一件迟疑、重复或未完成的事情；结尾落在具体动作、沉默或仍未得到回答的问题上。

### Metadata
- Source: user_feedback
- Related Files: Terrias/World/乌娜.md
- Tags: narrative-copy, natural-writing, terrias, ai-flavor
- Pattern-Key: prose.end_on_action_not_explanation
- Recurrence-Count: 1
- First-Seen: 2026-08-20
- Last-Seen: 2026-08-20

---

## [LRN-20260821-001] knowledge_gap

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The Terrias inventory helper counts only `Data/Card/terrias.csv`, not every shipped card CSV.

### Details
`extract-terrias-inventory.ps1` reported 66 cards, matching the 66 rows in
`Terrias/Data/Card/terrias.csv`. Recursive CSV inspection found 74 actual data
rows because `wuna.csv`, `loneer.csv`, `columbina.csv`, and `cursecard.csv`
contribute another eight combat cards or skills. A repository-wide combat audit
must enumerate every CSV under the table directory rather than trust the current
helper's card total.

### Suggested Action
Update the inventory helper through the Terrias skill-evolution workflow to
aggregate all `Terrias/Data/Card/*.csv` files and distinguish ordinary pack
cards, role skills, templates, and curse cards.

### Metadata
- Source: error
- Related Files: .codex/skills/terrias-mod-dev/scripts/extract-terrias-inventory.ps1, Terrias/Data/Card
- Tags: terrias, inventory, cards, validation
- Pattern-Key: terrias.inventory.aggregate_all_table_csvs
- Recurrence-Count: 1
- First-Seen: 2026-08-21
- Last-Seen: 2026-08-21

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: The inventory helper now aggregates every CSV under each Terrias Data table directory.

---

## [LRN-20260821-002] knowledge_gap

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: backend

### Summary
Terrias battle lifecycle names do not precisely describe the current native boundaries.

### Details
In the current game snapshot, `Fight_Start.Init` schedules the actual
`FightStart*` EventCenter signals for 0.3 seconds later, so an after-hook on
`Fight_Start.Init` is a pre-signal opening boundary rather than a completed
fight-start boundary. Conversely, `UIManager.ShowTip` invokes its callback
synchronously, so an after-hook on `Fight_PlayerTurn.Init` occurs after
`StartRound`, card draw, and `StartRoundEnd`; it is a player-round-ready
boundary rather than a round-start boundary.

### Suggested Action
Split exact native observation names from semantic lifecycle phases. Migrate
features to explicit opening, round-start-signal, round-ready, outcome-entering,
settling, and ended phases with documented invariants.

### Metadata
- Source: conversation
- Related Files: AuraSharedCore/AuraBattleLifecycleRouter.cs, 开发参考资料/反编译文件夹v1.0.24605918/Witch/Fight_Start.cs, 开发参考资料/反编译文件夹v1.0.24605918/Witch/Fight_PlayerTurn.cs
- Tags: terrias, lifecycle, eventcenter, timing
- Pattern-Key: terrias.lifecycle.name_exact_native_boundaries
- Recurrence-Count: 1
- First-Seen: 2026-08-21
- Last-Seen: 2026-08-21

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: Replaced the ambiguous shared lifecycle with exact opening, native signal, ready, round, recurrent action-loop, and outcome phases; migrated Terrias and AuraToolsExp consumers.

---

## [LRN-20260821-003] best_practice

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: low
**Status**: pending
**Area**: tests

### Summary
Keep build and validation commands in separate tool calls for readable output.

### Details
One development check combined a production build and the architecture gate in
the same PowerShell command. Both passed, but this conflicts with the workspace
rule that avoids separator-chained shell commands and makes failures harder to
attribute.

### Suggested Action
Run each build or gate as its own command/tool call, especially for serial
Terrias and shared-runtime validation.

### Metadata
- Source: error
- Related Files: tools/Test-TerriasArchitecture.ps1
- Tags: powershell, validation, workflow
- Pattern-Key: tooling.one_validation_per_command
- Recurrence-Count: 2
- First-Seen: 2026-08-21
- Last-Seen: 2026-08-21

The same mistake recurred in a final diff-plus-retired-token audit; keep these
as separate tool calls even when both are read-only.

---
## [LRN-20260821-PCM] best_practice

**Logged**: 2026-08-21T12:00:00+08:00
**Priority**: high
**Status**: pending
**Area**: backend

### Summary
Cooperative media processing must not reserve the final payload size in the
requesting Hook before its slices begin.

### Details
Moving AudioClip sampling across frames was not sufficient while the capture
constructor still created `MemoryStream(44 + valueCount * 2)`: that capacity
allocates the complete PCM payload immediately on the native audio Hook. The
final design stores bounded PCM chunks per slice and joins them only inside the
background finalizer.

### Suggested Action
For future cooperative decoders and encoders, audit constructor-time capacity,
array, and buffer allocations in addition to the work loop. Keep request
registration O(1), bound every main-thread slice, and allocate the final joined
payload only on a worker.

### Metadata
- Source: error
- Related Files: AuraToolsExp-Dev/Features/MatchRecords/Replay/Capture/ReplayFactCaptureV10.cs
- Tags: performance, audio, hooks, allocation, cooperative-work
- Pattern-Key: performance.cooperative_media_no_eager_full_buffer
- Recurrence-Count: 1
- First-Seen: 2026-08-21
- Last-Seen: 2026-08-21

---
