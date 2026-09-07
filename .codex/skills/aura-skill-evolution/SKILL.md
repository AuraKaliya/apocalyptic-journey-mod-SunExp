---
name: aura-skill-evolution
description: Audit, clean up and iterate project skills from verified Aura development evidence, including routing, references, helper ownership, stale contracts and representative task evaluation. Use for skill maintenance across Terrias, AuraToolsExp, shared runtimes and training tools.
---

# Aura Skill Evolution

Use with the available skill-creator guidance. Preserve project knowledge that
changes decisions; keep current facts queryable and incident history separate.

## Evidence and scope

Inspect current code, manifests, test ownership and relevant recent commits.
For runtime incidents include the failed stage, successful/absent branches,
authority, lifecycle and matching host evidence. Treat .learnings as historical
claims to revalidate.

Classify each proposed change as a trigger, invariant, conditional reference,
deterministic tool/test, manual acceptance case or historical note.
[Evidence packets](references/evolution-log-pattern.md) describe this process.
[Historical anchors](references/stale-anchor-registry.md) are for archaeology,
not normal development routing.

## Promotion criteria

Promote a lesson only if it:
- recurs as a class of tasks;
- would have prevented a concrete wrong decision;
- states applicability and a meaningful non-applicable case;
- has executable or observable enforcement where the invariant warrants it.

Keep generic programming advice and resolved incident narratives out of skill
bodies. Preserve user-authored aesthetic or product constraints. Do not infer
new approval gates or expand authorization from a past example.

## Maintenance

- Give each responsibility one canonical owner. Reference shared guidance
  directly rather than requiring chains of unrelated skill bodies.
- Keep descriptions selective. Put conditional procedures in linked references.
- Read current versions, consumers and validation inventory through
  `tools/Get-AuraProjectContext.ps1`; link the owning source for details.
- Product validators belong in tools. Skill-specific diagnostic helpers may
  remain with the skill when no product gate depends on their location.
- Before renaming, inspect active callers and references. Update them in one
  cutover; retain an alias only for a demonstrated compatibility need with an
  explicit retirement condition. Historical logs need not be rewritten.
- Retire source snapshots and completed migration checks when no current
  contract remains. Do not remove supported-data, authority or ownership tests
  merely because they began as a regression.

## Validation

Follow [tool setup and checks](references/validation.md).
Use [representative tasks](references/task-evaluation.md) to check actual
routing, contract selection and validation scope. Metadata/link validation
does not prove good decisions. For substantial changes, perform an independent
read-only forward test through an available subagent, using realistic requests
and raw artifacts without supplying expected answers or prior conclusions.
