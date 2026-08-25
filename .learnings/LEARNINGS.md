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
- Recurrence-Count: 3
- First-Seen: 2026-08-21
- Last-Seen: 2026-08-25

The same mistake recurred in a final diff-plus-retired-token audit; keep these
as separate tool calls even when both are read-only.
It recurred again while validating the poster skill by chaining
`quick_validate.py` and `git diff --check`; run each validation as its own tool
call even when the first command is expected to finish immediately.

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

## [LRN-20260825-001] best_practice

**Logged**: 2026-08-25T00:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: backend

### Summary
Nested temporary mutations of one shared runtime target require one
generation-aware authoritative owner rather than consumer-local "original"
snapshots.

### Details
AuraTools card effects and Terrias exit animation both mutated the same pooled
Renderer. Each consumer captured a different material as its baseline, so an
out-of-order release could later restore an already superseded or destroyed
material. The symptom appeared at pool reuse, but the first incorrect owner was
the duplicated restoration authority.

### Suggested Action
Classify the mutation model first. For nested temporary mutations, key one
coordinator by physical root, logical generation, and exact target/property;
record out-of-order release as pending, drain in LIFO order, and quarantine a
target whose rollback or baseline cannot be proved. Do not apply this stack
model to persistent selection, aggregation, or authoritative snapshots.

### Metadata
- Source: runtime_log_and_fix
- Related Files: AuraSharedCore/AuraPresentationMaterialCoordinator.cs, .codex/skills/terrias-shared-runtime-dev/references/shared-mutable-runtime-ownership.md
- Tags: shared-runtime, ownership, unity, pooling, material
- Pattern-Key: shared.mutable_target.single_generation_owner
- Recurrence-Count: 1
- First-Seen: 2026-08-25
- Last-Seen: 2026-08-25

### Resolution
- **Resolved**: 2026-08-25T00:00:00+08:00
- **Notes**: Distilled into the shared mutable runtime ownership reference and enforced by Core plus cross-consumer behavior tests.

---

## [LRN-20260825-002] best_practice

**Logged**: 2026-08-25T00:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: backend

### Summary
A deferred or skipped cleanup branch is valid only when responsibility is
durably transferred and later drained.

### Details
Repeated logs said material restoration was deferred or skipped because a newer
owner was active, but the old implementation had no durable pending obligation,
successor owner, wake-up event, or convergence assertion. The warning described
abandoned cleanup as though it were a safe delay.

### Suggested Action
For every defer, skip, sent, or handled branch, identify the new owner, pending
record, drain trigger, and final postcondition. If any is absent, repair the
ownership model instead of adding another warning, timeout, or retry.

### Metadata
- Source: runtime_log_and_fix
- Related Files: .codex/skills/terrias-complete-solution-gate/SKILL.md, .codex/skills/terrias-shared-runtime-dev/references/shared-mutable-runtime-ownership.md
- Tags: lifecycle, cleanup, responsibility, diagnostics
- Pattern-Key: lifecycle.deferred_work_requires_durable_obligation
- Recurrence-Count: 1
- First-Seen: 2026-08-25
- Last-Seen: 2026-08-25

### Resolution
- **Resolved**: 2026-08-25T00:00:00+08:00
- **Notes**: Added to the highest-priority solution gate and the shared ownership reference.

---

## [LRN-20260825-003] knowledge_gap

**Logged**: 2026-08-25T00:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: backend

### Summary
A synthetic combat object is not native-equivalent until every owner index,
manager, queue, executor route, presentation surface, and cleanup path agrees
on its identity.

### Details
Projection status objects existed, acted, damaged enemies, and completed turns,
but their Partner statuses were absent from the actual owner's native
`RoleStatusMap`. `ForEachObject` selected each target as executor status and
`TrySendOnlineEvent` treated the projection self as non-local, sent the event,
and skipped local Buff mutation. A high-level successful turn therefore hid a
missing target-side effect.

### Suggested Action
Audit the complete native-equivalence surface for synthetic objects. For the
current Partner runtime, register the status exactly once under its real owner,
repair stale/duplicate mappings, and clean every owner list on failure or
teardown. Never invent an executor-wide `Vars["Online"]` override to fix one
object's locality; preserve a native pre-existing value unchanged.

### Metadata
- Source: runtime_log_decompile_and_fix
- Related Files: Terrias-Dev/Mechanics/CompanionNativeStatusRouting.cs, .codex/skills/terrias-architecture-dev/references/native-synthetic-runtime-objects.md
- Tags: native-integration, projection, partner, scriptexecutor, multiplayer
- Pattern-Key: native.synthetic_object.complete_owner_and_locality_surface
- Recurrence-Count: 1
- First-Seen: 2026-08-25
- Last-Seen: 2026-08-25

### Resolution
- **Resolved**: 2026-08-25T00:00:00+08:00
- **Notes**: Distilled into the native synthetic runtime object reference and covered by Terrias owner-route and execution-scope behavior tests.

---

## [LRN-20260825-004] best_practice

**Logged**: 2026-08-25T00:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: tests

### Summary
Lifecycle and native-routing regressions require interleaving and target/locality
matrices; top-level success and isolated happy paths are insufficient.

### Details
The pool defect required a dynamic-effect, exit-animation, out-of-order-release,
reuse sequence. The Projection defect allowed multiple successful actions and
enemy effects while missing the self-local Buff path. Tests that asserted only
one acquire/release or one completed actor turn would preserve both defects.

### Suggested Action
Test release permutations, duplicate operations, partial failure, rollback,
external mutation, destroyed targets, and generation conflicts for lifecycle
state machines. For synthetic execution, separately prove local self mutation,
non-local target RPC routing, failed-init cleanup, and exact context restoration
on exception. Keep architecture gates limited to placement and dependencies.

### Metadata
- Source: regression_design
- Related Files: AuraSharedCore.Tests/CorePresentationMaterialCoordinatorTests.cs, Terrias-Dev.Tests/Program.cs, .codex/skills/terrias-mod-dev/references/validation-rules.md
- Tags: tests, interleaving, locality, failure-paths, behavior
- Pattern-Key: tests.lifecycle_and_locality_matrix_over_top_level_success
- Recurrence-Count: 1
- First-Seen: 2026-08-25
- Last-Seen: 2026-08-25

### Resolution
- **Resolved**: 2026-08-25T00:00:00+08:00
- **Notes**: Added to the validation impact and behavior matrices; the implementation already has focused state-machine and routing regression coverage.

---
