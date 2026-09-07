# Training, Simulation and Recovery

Read only the applicable authoritative documents:

- [Simulation and Foundation training](../../../../docs/AuraCombatAI/06-权威模拟与底模训练.md)
- [Training/model qualification](../../../../docs/AuraCombatAI/05-训练与模型门禁.md)
- [Training versus game validation](../../../../docs/AuraCombatAI/09-训练与游戏主体验证分离.md)
- [Content packages](../../../../docs/AuraCombatAI/10-内容MOD训练包与玩家适配器.md)
- [Worker data flow and persistence](../../../../docs/AuraCombatAI/13-独立底模训练器数据流与优化优先级.md)

The index labels planned Rule-IR/export certification work explicitly. Do not
treat an existing content package as certified under a proposed contract.

## Diagnose the first failed stage

Separate job/preflight, simulation, replay selection, feature encoding,
teacher/MLP training, screening, confirmation, qualification, checkpoint
commit and package publication. Inspect each stage's input identity, expected
output, actual failure and retained recovery state. A progress counter or
completed campaign is not proof of promotion.

Online recorder data, training episodes and visual battle-replay documents have
different contracts. Trace their writers and readers before changing a schema.

## Persistence and reset

Check primary and backup integrity, generation identity, checksums, token
catalogs and artifact reachability. Incomplete commits must recover
deterministically. Do not delete an old shard/index until the replacement is
committed and verified by the real reader.

Before reset or GC, resolve the exact configured storage root and scope, prove
containment, identify live writers and invalidate recovery pointers according
to the durable reset protocol. Preserve required user datasets; an unrelated
skill, UI or build task does not authorize training-state deletion.

Keep model quality and resumable training state distinct. Latest training,
pending candidate, qualified model and active runtime model can have different
identities; preserve those meanings through recovery and UI.

## Focused checks

- `tools/Test-AuraFoundationTrainerStorage.ps1`: checkpoint/replay/reset behavior.
- `tools/Test-AuraCombatTrainingArtifacts.ps1`: model/rule/training packages.
- `tools/Test-AuraCombatSimulationAcceptance.ps1`: deterministic CLI boundary.
- `tools/Test-AuraFoundationTrainer.ps1`: external worker integration.
- `tools/Test-AuraFoundationArchiveMaintenance.ps1`: explicit archive maintenance.

Use small isolated fixtures for corruption, interruption, backup recovery,
mismatched identity and migration tests. Do not use real player training
directories as test fixtures. Run performance or training-quality comparisons
only with controlled inputs and the relevant acceptance budget.
