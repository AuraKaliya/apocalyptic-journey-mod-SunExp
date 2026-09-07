# Live Decision and Execution

Read [system architecture](../../../../docs/AuraCombatAI/01-系统架构.md),
[observation and execution safety](../../../../docs/AuraCombatAI/02-观测与执行安全.md)
and [search](../../../../docs/AuraCombatAI/03-风险敏感根抽样PUCT.md) for the
specific layer being changed.

Trace observation -> candidate generation -> planning -> immutable receipt ->
native transaction -> observed result -> terminal recording. Check freshness
at execution, not only when search begins. Cancellation, a changed scene or a
player action must not leave a queued decision capable of committing later.

Keep online authority, shadow diagnostics and training objectives separate.
Verify the current active decision path in source before attributing behavior
to a model mentioned in a target-design document.

Use legal, adversarial and lifecycle cases: stale receipt, changed cost/target,
player interruption, terminal battle, canceled search and failed native
transaction. Model health only records failures the current model contract
owns. UI or gameplay transaction failures should not silently become training
labels or model technical faults.

[Acceptance](../../../../docs/AuraCombatAI/07-测试与发布验收.md) defines
offline and real-game evidence.
`tools/Test-AuraCombatAi.ps1` covers shared AI behavior;
`tools/Test-AuraCombatKnowledge.ps1` covers knowledge changes;
`tools/Test-AuraCombatSimulationAcceptance.ps1` covers CLI behavior.
Include the owning tool suite for live-session adapter changes.
