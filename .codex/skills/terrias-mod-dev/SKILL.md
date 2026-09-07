---
name: terrias-mod-dev
description: Develop Terrias content and mechanics in this repository, including C# CSV entry points, Data/Text tables, cards, buffs, relics, roles, packs, Spirit systems and Endless modes. Use Aura specialist skills for tool modules, shared runtime contracts, replay or training.
---

# Terrias Mod Dev

Terrias is the content MOD. Terrias/ is the shipped surface and Terrias-Dev/ is
its C# implementation. The XLua bridge in Entry is host interop, not a separate
production Lua implementation path.

## Work from the current content

1. Inspect the affected Data/Text rows, Scripting entry points and owning
   Mechanics/Application service. Use `tools/Get-TerriasInventory.ps1` for
   table-wide counts; do not infer totals from one CSV.
2. Choose only the relevant reference:
   - [CSV schema](references/csv-schema.md): Card, Buff, Relic and CardPack fields.
   - [C# authoring](references/csharp-authoring-boundaries.md): entry points,
     read-only base data, host interop and runtime values.
   - [Role/dialogue expansion](references/expansion-role-dialogue-event.md).
   - [Map-event expansion](references/solar-event-expansion.md).
   - [Game reference](references/game-reference-index.md): Managed/decompile evidence.
   - [External practice index](references/external-best-practice-index.md):
     optional upstream research for a specific Unity/design question.
   - [Feature navigation](../aura-project-dev/references/project-map.md):
     Spirit, artifacts, Projection, Endless Sea and Endless Abyss.
3. Keep behavior in `CS.Terrias.Dll.Scripting.*` entry points. CSV script
   columns delegate; reusable behavior belongs to the current C# layers.
4. Keep Data/Text identity, descriptions, resources and actual behavior aligned.
5. Select checks from the
   [shared validation guide](../aura-project-dev/references/validation.md).

## Content contracts

- Use full IDs for Terrias-defined content. Match the actual table schema;
  tables with localized counterparts require aligned Data/Text rows.
- Keep battle/run state out of base CSV and `IDataConfig.data`. Runtime
  overrides use Vars; compose persistent payloads from copies.
- Prefer existing domain helpers to inline CSV logic. Do not expose Terrias
  internal helpers as a framework for AuraToolsExp.
- Terrias owns mechanics and required presentation. Optional voice/CG files
  are declarations discovered by AuraToolsExp through the shared contract.
- Leave Text/Relic.Tag blank unless a visible label is intended; it is distinct
  from Data/Relic.PackBelong.

## Focused routes

- [Architecture](../terrias-architecture-dev/SKILL.md): new boundaries, hooks,
  Managed compatibility, synthetic native identity or execution routing.
- [Events](../terrias-event-dev/SKILL.md): ordinary/story/map-visible events.
- [Solar Memory](../terrias-solar-memory-dev/SKILL.md): mode preparation and commit.
- [Shared runtime](../aura-shared-runtime-dev/SKILL.md): cross-product contracts.
- [Visual runtime](../aura-visual-runtime-dev/SKILL.md): Unity presentation.
- [Complete solution](../aura-complete-solution-gate/SKILL.md): repairs or cutovers.

These are conditional routes, not a list to load for every content task.
[Card](assets/templates/card-row.md), [Buff](assets/templates/buff-row.md),
[Relic](assets/templates/relic-row.md), and
[role/event](assets/templates/role-dialogue-event-checklist.md) templates are
authoring aids; verify them against current tables before use.
