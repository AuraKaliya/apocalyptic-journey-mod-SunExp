---
name: aura-visual-runtime-dev
description: Develop Unity presentation in Aura products and shared runtimes, including CG, card visuals, shaders, bundles, UI pools and rendering lifecycles. Use for runtime visuals; use the art skills for bitmap generation and the replay skill for replay diagnosis.
---

# Aura Visual Runtime Dev

First identify whether the changed behavior belongs to content presentation,
tool configuration, or a shared presentation service.

## Ownership and evidence

- Terrias owns required content presentation: opening director, Wuna orbit fire,
  Star Score feedback, map-node art and content-owned animated icons.
- Terrias may carry optional voice/CG declarations and media; AuraTools discovers
  and configures them through shared contracts.
- AuraTools owns replacement skins, card-frame themes, configurable card
  effects and tool defaults.
- Shared domains own resource identity, playback, networking, caches and shared
  presentation lifecycles.

Inspect current registries, source and tests for the selected owner. Use
`tools/Get-AuraProjectContext.ps1` for current protocol sources rather than
copying version numbers into guidance.

## Focused references

- [Registry and bundles](references/visual-registry-and-bundle.md):
  visual.registry.json, shader assets, generated Unity workspaces and builds.
- [Card presentation](references/card-visual-skins-and-effects.md):
  native surfaces, explicit mapping, frame effects and card lifecycle.
- [CG and shared resources](references/skill-cg-and-shared-resources.md):
  unified role/card/event signals, scene planning and media ownership.
- [UI and performance](references/runtime-visual-ui-and-performance.md):
  Terrias HUD, icons, Wuna orbit fire and cached transient UI.
- [Shared mutable ownership](../aura-shared-runtime-dev/references/shared-mutable-runtime-ownership.md):
  overlapping Renderer/material mutation or pooled generations.

For replay-host rendering, first-frame gates or URP/RenderGraph failures,
[replay](../aura-battle-replay-dev/SKILL.md) owns diagnosis and acceptance.
For product settings and module UI, use [AuraTools](../aura-tools-dev/SKILL.md).

## Runtime invariants

- Keep selection in registries/catalogs and Unity mutation in the owning runtime.
  Reuse established resource, bundle, shader and material caches.
- Each bundle belongs to its owner. Generated Unity editor projects stay
  excluded from MOD C# compilation.
- Wuna orbit fire remains Terrias-local. Keep Star Score feedback distinct from
  the generic tool effect feature.
- Do not let effects, exit animations or pooled views independently restore an
  original material for the same Renderer; use the shared coordinator.
- Card visuals match explicit owner-qualified card IDs. Pack/rarity choices
  are editor expansion. A theme may seed a versioned mapping preset once,
  without overwriting subsequent user edits.
- Keep visual overlays non-blocking for raycasts and honor runtime budgets.
- A shared CG scene or media lease must release on replacement, disable,
  cancellation and teardown; verify subsequent reuse.

## Validation

Select the owning tests and bundle build from the
[impact guide](../aura-project-dev/references/validation.md). Build products
once when publishing C# changes. Build a visual bundle only for changed bundle
sources. Run Unity/game acceptance for render, layout, raycast, async media or
lifecycle changes; .NET/member-existence checks do not prove those boundaries.
