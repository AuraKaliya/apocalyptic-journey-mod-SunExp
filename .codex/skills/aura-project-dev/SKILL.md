---
name: aura-project-dev
description: Orient and route work in the Aura workspace across Terrias, AuraToolsExp, shared runtimes, validation, and developer tooling. Use for repository analysis, cross-product work, or uncertain ownership; an already identified domain can use its specialist directly.
---

# Aura Project Dev

Use this repository-local skill to locate the owner, current contract, and
appropriate checks. It is a navigation entry, not a prerequisite for every
specialist task.

## Working map

- [Project map](references/project-map.md): products, training tools, shared
  components, document entry points, and Terrias feature navigation.
- [Validation](references/validation.md): impact selection, build/publication
  semantics, behavior ownership, and runtime acceptance.
- Run `tools/Get-AuraProjectContext.ps1` for current manifest-derived facts.
  Use `-AsJson` when composing tools.

## Route by changed responsibility

| Responsibility | Owning skill |
| --- | --- |
| Defect, migration, compatibility repair or debt cleanup | [Complete solution](../aura-complete-solution-gate/SKILL.md), then the affected domain |
| Terrias CSV, localization, content or mechanics | [Terrias content](../terrias-mod-dev/SKILL.md) |
| Terrias C# placement, synthetic objects or native execution routing | [Terrias architecture](../terrias-architecture-dev/SKILL.md) |
| Ordinary events and story chains | [Events](../terrias-event-dev/SKILL.md) |
| Solar Memory preparation, map or role commit | [Solar Memory](../terrias-solar-memory-dev/SKILL.md) |
| Shared storage, resource, lifecycle, network or DLL contract | [Shared runtime](../aura-shared-runtime-dev/SKILL.md) |
| Tool modules, settings, presets, discovery or tool UI | [AuraTools](../aura-tools-dev/SKILL.md) |
| Gameplay AI, simulation, training, model or checkpoint | [Combat AI](../aura-combat-ai-dev/SKILL.md) |
| Recorded battle playback, seeking or video export | [Battle replay](../aura-battle-replay-dev/SKILL.md) |
| Unity visuals, CG, shaders, pooled views or bundles | [Visual runtime](../aura-visual-runtime-dev/SKILL.md) |
| Card/relic bitmap art | [Card art](../terrias-card-art-style/SKILL.md) |
| Terrias topic posters | [Posters](../terrias-poster-design/SKILL.md) |
| Project skill changes | [Skill evolution](../aura-skill-evolution/SKILL.md) |

For a multi-domain task, one skill owns the use case; load the specific
references needed at other boundaries. Following a link to a shared reference
does not require loading every referenced skill body.

## Evidence and completion

Current code, schemas, consumer manifests, and test matrices define operational
facts. Managed defines compilation; a matching decompile and real runtime
observations establish host semantics. Accepted design documents explain
intent; check their implementation status. .learnings and old commits are
historical evidence, not unconditional current instructions.

Keep source, supported data, relevant tests, and shipped artifacts aligned
when their contract changes. A documentation-only or skill-only task does not
require rebuilding otherwise unchanged products.
