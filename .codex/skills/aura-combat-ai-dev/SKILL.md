---
name: aura-combat-ai-dev
description: Develop Aura gameplay AI, authoritative simulation, Foundation training, model artifacts, checkpoint recovery and training datasets. Use for online decision or offline training contracts; recorded battle viewing and video export belong to the battle-replay skill.
---

# Aura Combat AI Dev

Start with the [AI contract index](../../../docs/AuraCombatAI/README.md).
It distinguishes current production behavior, training/shadow capabilities and
planned designs. A target architecture is not evidence that an online path is
active or a content package has been certified.

## Select the execution surface

| Surface | Owner |
| --- | --- |
| Player-equivalent observation, legal actions, search, value and risk | AuraCombatAiShared |
| Online session, native action transactions and bounded recorder | AuraToolsExp-Dev/Features/AutoBattle |
| Deterministic simulation and scenarios | AuraCombatSimulationShared and AuraCombatSimulation.Cli |
| External training, checkpoint/replay recovery and progress | AuraFoundationTrainer.Worker |
| Job configuration and supervision | AuraFoundationTrainer.ControlCenter |
| Simulation viewing | AuraFoundationTrainer.SimulationViewer |
| Shared content/rule semantics | AuraGameDataShared and declared AI content contracts |

Read [decision/execution](references/live-decision.md) or
[training and recovery](references/training-and-simulation.md) for the actual
surface. Avoid loading the playback skill for training Replay Warehouse data.

## Invariants

- Online decisions use only information available to the player. Validate action
  legality and session/sequence/target/interaction state again at commit.
- The live session owns online execution. UI/transaction failures return control
  according to that contract; they do not automatically prove model failure.
- Training, evolution and large-scale simulation run outside the game process.
  Online recording remains bounded and seals on the authoritative terminal event.
- Preserve deterministic seeds, frozen rule/content identity and real transition
  labels. Hypothetical candidates are not observed executed outcomes.
- Training losses, shadow quality and screening are not promotion evidence.
  Qualification requires the actual declared comparison and acceptance stages.
- Recovery and GC preserve all reachable retained generations, including valid
  backups. Uncertain integrity blocks destructive reclamation.
- Read current protocol/feature/schema values from their owning sources and
  document index. Do not bake today's dimensions or versions into this skill.
- Change writers, supported readers, fixtures, examples and docs together for a
  protocol cutover; follow the
  [complete-solution gate](../aura-complete-solution-gate/SKILL.md).

## Validation

Inspect the shared matrix's combat-ai/foundation steps and their cost.
Select behavior, knowledge, training-artifact, simulation-acceptance,
storage-recovery and worker-integration checks according to the changed boundary.
Full worker runs and archive maintenance are explicit work, not default checks
for unrelated tool UI or shared edits.

Use the [impact guide](../aura-project-dev/references/validation.md) for
publication. Build changed trainer executables with
`tools/Build-AuraFoundationTrainer.ps1`; game product builds do not publish
the trainer. Runtime model qualification requires the documented game evidence
in addition to offline tests.
