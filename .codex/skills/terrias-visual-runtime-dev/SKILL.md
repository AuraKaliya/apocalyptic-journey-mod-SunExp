---
name: terrias-visual-runtime-dev
description: Project-local skill for editing or reviewing the Terrias/AuraTools visual split, including Terrias-required visuals, AuraTools-owned CG and card visuals, shared presentation lifecycles, VisualBundles, shaders, map-node art, Wuna orbit fire, Star Score feedback, and visual validation in Witch's Apocalyptic Journey.
---

# Terrias Visual Runtime Dev

Use this skill inside this repository when visual behavior is runtime-bound, not
only image generation. Pair it with `terrias-mod-dev`; pair it with
`terrias-card-art-style` only when generating or replacing source bitmap art.
Pair it with `terrias-shared-runtime-dev` when Skill CG or shared resource
manifests change.

## Workflow

1. Classify ownership before implementation:
   - Terrias-required content presentation: opening director animation,
     Wuna orbit fire, Star Score HUD/card-use feedback, map-node art, and
     content-owned animated icons.
   - Terrias-carried optional media: Terrias role voice and Skill/card-use/Feast
     CG declarations/assets under `SharedResources`; AuraTools discovers and
     configures them.
   - AuraTools-owned media: tool defaults, replacement skins, card-frame themes,
     and configurable card dynamic effects.
   - Shared foundation: card presentation lifecycle, CG/audio protocols,
     playback/network identity, resource paths, caches, and hook routing.
   - Map-node card art, animated buff/blessing/enemy icons, Star Score HUD, or
     Wuna orbit fire.
2. Inspect the smallest current surface:
   - `Terrias/visual.registry.json`
   - `Terrias-Dev/VisualAssets/*`
   - `Terrias-Dev/Hooks/Visual/*`
   - `Terrias-Dev/Hooks/Ui/*`
   - `AuraSharedCore/AuraCardPresentationRuntime.cs`
   - `AuraToolsExp/card-visual.registry.json`
   - `Terrias/SharedResources/aura.discovery.json`, `audio.registry.json`, and
     `cg.registry.json`
   - `AuraToolsExp/SharedResources/cg.registry.json` for tool-owned entries
   - `AuraToolsExp/SharedResources/CardVisual/*`
   - `AuraToolsExp-Dev/Features/CardVisual/*`
   - `AuraToolsExp-Dev/Features/SkillCg/*`
   - `AuraToolsExp-Dev/VisualAssets/*`
   - `tools/Build-TerriasVisualBundle.ps1`
   - `tools/Build-AuraToolsVisualBundle.ps1`
3. Load references as needed:
   - `references/visual-registry-and-bundle.md`: registry, bundle, shaders,
     cache, and build pipeline.
   - `references/card-visual-skins-and-effects.md`: card skins, frame effects,
     runtime attachment, dynamic frame skins, and card-use animation lifecycle.
   - `references/skill-cg-and-shared-resources.md`: Skill CG manifests,
     AuraCgShared, shared resources, and AuraTools consumption.
   - `references/runtime-visual-ui-and-performance.md`: UI visuals, animated
     icons, Wuna orbit fire, Star Score HUD, resource caches, and performance.
4. Keep declarations data-driven where possible. Put visual selection in
   registries and catalogs; put Unity object mutation in `Hooks/Visual` or
   `Hooks/Ui`; put reusable matching and rules in `Mechanics`.
5. Run visual validation before finishing.

## Hard Rules

- Do not replace `Terrias/visual.registry.json` declarations with one-off
  hard-coded runtime paths.
- Do not bypass `TerriasResourceCache`, `AssetBundleCache`, or shader/material
  cache helpers for repeated visual loads.
- Keep each VisualBundle aligned with its owner. Terrias bundles only required
  content visuals; AuraTools bundles optional CG/card-visual shaders and
  materials.
- Exclude generated UnityProject editor sources from product MOD compilation;
  they require UnityEditor and are built only by the VisualBundle script.
- Terrias may ship its own optional voice/CG files and declarations only under
  the shared discovery contract. It must not own their active registration,
  playback, networking, settings, or editor runtime. Replacement skins,
  card-frame themes, configurable effects, and tool defaults stay in AuraToolsExp.
- Opening director animation remains Terrias-owned. Wuna orbit fire remains a
  Terrias-only implementation and must not be generalized or split.
- Keep Star Score card-use feedback distinct from the generic AuraTools card
  dynamic-effect feature, even if their shaders share implementation ideas.
- Card visuals use explicit owner-qualified card ids at runtime. Card-pack and
  rarity choices are editor batch expansion only; do not restore runtime pack,
  rarity, icon-prefix, suffix, wildcard, or default-whitelist matching.
- A card-frame theme may seed a versioned, theme-bound mapping preset on first
  load. Never overwrite later user edits during ordinary startup.
- Keep visual-only overlays non-blocking for raycasts.
- Respect performance settings and counters for expensive visuals, especially
  Wuna orbit fire and HUD/shader updates.
- Route new shared visual protocols through `terrias-shared-runtime-dev`.

## Validation

Run the affected checks serially:

```powershell
tools\Build-TerriasDll.ps1
tools\Build-AuraToolsExpDll.ps1
tools\Test-TerriasArchitecture.ps1
tools\Test-TerriasCSharp.ps1
tools\Build-TerriasVisualBundle.ps1 # Terrias-required visual assets
tools\Build-AuraToolsVisualBundle.ps1 # AuraTools CG/card-visual assets
tools\Test-AuraToolsExp.ps1
.codex\skills\terrias-mod-dev\scripts\validate-terrias.ps1
```

When Skill CG or shared resource manifests change, also run the shared release
checks from `terrias-shared-runtime-dev`.
