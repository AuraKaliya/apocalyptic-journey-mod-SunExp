---
name: terrias-visual-runtime-dev
description: Project-local skill for editing or reviewing Terrias visual runtime systems, including Terrias visual.registry.json, VisualBundles, shaders, card visual skins and frame effects, Skill CG registration/playback, map-node card art, animated icons, Wuna orbit fire, Star Score HUD visuals, visual resource caches, and visual validation in Witch's Apocalyptic Journey.
---

# Terrias Visual Runtime Dev

Use this skill inside this repository when visual behavior is runtime-bound, not
only image generation. Pair it with `terrias-mod-dev`; pair it with
`terrias-card-art-style` only when generating or replacing source bitmap art.
Pair it with `terrias-shared-runtime-dev` when Skill CG or shared resource
manifests change.

## Workflow

1. Classify the visual surface:
   - Visual registry, VisualBundle, shader, material, or runtime asset cache.
   - Card visual skin, card frame/effect material, or runtime card attachment.
   - Skill CG, Aura CG registry, shared resource package, or tool-consumed CG.
   - Map-node card art, animated buff/blessing/enemy icons, Star Score HUD, or
     Wuna orbit fire.
2. Inspect the smallest current surface:
   - `Terrias/visual.registry.json`
   - `Terrias-Dev/VisualAssets/*`
   - `Terrias-Dev/Hooks/Visual/*`
   - `Terrias-Dev/Hooks/Ui/*`
   - `Terrias-Dev/Features/SkillCg/*`
   - `Terrias/SharedResources/package.json`
   - `Terrias/SharedResources/cg.registry.json`
   - `tools/Build-TerriasVisualBundle.ps1`
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
- Keep VisualBundle source and shipped bundle aligned. Rebuild the bundle after
  shader, material, or bundled CG changes.
- Keep Skill CG resources in the shared package/CG registry path. Do not make
  AuraTools scan private Terrias folders as a substitute for registration.
- Keep visual-only overlays non-blocking for raycasts.
- Respect performance settings and counters for expensive visuals, especially
  Wuna orbit fire and HUD/shader updates.
- Route new shared visual protocols through `terrias-shared-runtime-dev`.

## Validation

Run the affected checks serially:

```powershell
tools\Build-TerriasDll.ps1
tools\Test-TerriasArchitecture.ps1
tools\Test-TerriasCSharp.ps1
tools\Build-TerriasVisualBundle.ps1 # when VisualAssets, shaders, bundled CG, or bundle manifests change
.codex\skills\terrias-mod-dev\scripts\validate-terrias.ps1
```

When Skill CG or shared resource manifests change, also run the shared release
checks from `terrias-shared-runtime-dev`.
