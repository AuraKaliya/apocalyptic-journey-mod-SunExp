---
name: aura-tools-dev
description: Develop AuraToolsExp modules, settings, presets, local configuration, resource discovery, diagnostics and Unity toolbox UI. Route recorded battle playback to the replay skill and gameplay AI or training to the combat-AI skill.
---

# AuraTools Dev

AuraToolsExp is the tool product. Implement in AuraToolsExp-Dev and keep
AuraToolsExp package configuration/resources aligned. Tool code consumes shared
contracts and must not use Terrias internals as a framework.

## Locate the owning workflow

Read the [module contract](../../../docs/AuraToolsExp/toolbox-settings-and-module-architecture-design.md)
and [integration guide](references/modules-and-ui.md) for module changes.
Inspect the current module definitions, settings models, persistence codec and
feature runtime together. Do not copy a module count or private file layout
from an old document into new validation.

Specialist routes:

- [Battle replay](../aura-battle-replay-dev/SKILL.md): MatchRecords recording,
  playback, native presentation, seeking and media export.
- [Combat AI](../aura-combat-ai-dev/SKILL.md): online decisions, simulation,
  training, model installation and checkpoint/replay datasets.
- [Visual runtime](../aura-visual-runtime-dev/SKILL.md): CG, card visuals,
  shaders, bundles and rendering lifecycles.
- [Shared boundary](../aura-shared-runtime-dev/references/content-tool-shared-boundary.md):
  discovery, registration ownership and effective configuration.
- [Complete solution](../aura-complete-solution-gate/SKILL.md): repairs/cutovers.

## Module contracts

- One module definition owns identity, state and its settings surface. UI and
  preview consume the same production inventory.
- Keep registration defaults, tool defaults and player overrides distinct.
  Resetting an override restores defaults; it does not rewrite foreign sources.
- Preset codecs explicitly declare exported state, exclusions and dependencies.
  Import preflights references and applies atomically with rollback.
- Persist settings through existing shared/Core paths and notifications.
  Avoid independent caches, alternate settings writers or polling loops.
- Tool UI displays player-facing names and effective values. Do not expose
  private registration paths, protocol details or implementation choices unless
  needed for a real user decision.
- Async media and pooled rows require ownership, cancellation and teardown.
  Content interaction must not bubble into a backdrop close action.
- Diagnostics report actual host-loaded state and limitations. Missing reflection
  evidence must not be converted into invented success or universal failure.
- Adventure archives and battle replay may share a database while retaining
  separate table ownership and deletion semantics.

## Validation and publication

Use `tools/Test-AuraToolsExp.ps1` for module/config behavior and owned content.
Use the [impact guide](../aura-project-dev/references/validation.md) for shared,
ABI, packaging and specialist checks.

Publish changed product C# with one `tools/Build-MainSharedConsumers.ps1`
transaction. Trainer builds are separate. Verify changed Unity pages with the
production module inventory and representative sizes; .NET tests cannot prove
layout, input, media release or the next window reuse.
