# Card Visual Themes And Effects

Use this reference for AuraTools card frames, card backgrounds, dynamic card
effects, theme presets, and the shared card presentation lifecycle.

## Ownership

- `AuraSharedCore/AuraCardPresentationRuntime.cs` owns the semantic-free,
  owner-qualified lifecycle for combat, reward, display, shop, warehouse,
  dictionary, pack, and safe-box card surfaces.
- `AuraToolsExp/card-visual.registry.json` declares tool-owned themes, skins,
  preset mappings, dynamic effects, materials, textures, and editable ranges.
- `AuraToolsExp-Dev/Features/CardVisual/*` owns configuration, batch expansion,
  Unity visual application, restoration, and the player-facing editor.
- `AuraToolsExp/SharedResources/CardVisual/*` and the AuraTools VisualBundle own
  the optional visual resources.
- Terrias owns only content ids and necessary content presentation such as Star
  Score card-use feedback. It does not register generic card skins or effects.

## Whitelist Contract

Runtime configuration is an explicit map from `ownerModId:cardId` to a skin or
dynamic effect. There is no global default whitelist.

Card-pack and rarity selectors are editor conveniences. They resolve the native
catalog once and write the resulting explicit card ids. Runtime matching must
not inspect pack, rarity, icon prefix, suffix, or wildcard patterns.

## Theme Presets

A theme may declare a versioned `mappingPreset`. On the first successful load,
after the native card catalog is ready, AuraTools expands the preset to the
theme's explicit card map. The mapping then belongs to that theme profile and
is editable.

Ordinary startup never reapplies a preset to an initialized theme. A deliberate
reset command may replace that theme's map and update its applied preset
version. A card may belong to at most one frame theme at a time.

## Runtime Rules

- Keep resource ids and card ids stable and owner-qualified.
- Restore original sprites, textures, and materials when disabling a mapping or
  reusing a pooled card view.
- Keep visual-only overlays non-blocking for raycasts and directly adjacent to
  their source frame/face layer.
- Dynamic parameters are accepted only when declared by the effect's exposed
  range map and are clamped before material application.
- Load the AuraTools VisualBundle through the tool-owned bundle cache so Skill
  CG and card effects do not load the same bundle twice.

## Validation

Run `tools/Test-AuraToolsExp.ps1`, `tools/Test-ContentToolSharedBoundary.ps1`,
and both VisualBundle builders when shader or material ownership changes.
