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
dynamic effect. Defaults are also explicit, versioned entries in
`card-visual.registry.json`; there is no code-built or wildcard global
whitelist. `CardVisualSettings.json` stores only local overrides. An absent
effect override inherits the shipped default, a disabled override is a
tombstone, and “restore default” deletes the override.

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

The shipped Terrias defaults bind `Terrias_terrias_blazing_crown_collapse` to
the solar frame and `foil-holo`, and bind the four
`Terrias_terrias_stellar_overture_*` cards to the morning-star frame and
`stardust`.

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
- Presentation subscribers execute deterministically from low to high
  priority. Tool/user overrides run last. After common or attack card use, the
  shared lifecycle coalesces one next-frame pass across active/wait hand cards
  so native hand reflow cannot leave a pooled card on its base skin.
- Lightweight Terrias card-view rebinding must call the shared presentation
  lifecycle, not the Terrias-only router. Pool prepare/destroy emits a shared
  reset; bind emits a shared apply. The v4 dynamic-effect renderer uses the
  `frame` target with `native-frame-v1`: clone the effect material onto the
  actual `Front/FrontBack` Image or MeshRenderer and bind the current frame
  texture. Do not restore the retired detached `frameOverlay` or white
  full-card `cardFront` surface; both change the native sprite UV/mask contract.
- Replay card instances carry a read-only snapshot of their effective theme,
  skin, effect id, and clamped parameter map in runtime Vars. Applying that
  snapshot uses the same v4 renderer and never rewrites current player config.
- Outcome entry, battle settling, restart, and battle end all perform an
  idempotent hand-view teardown. A settlement screen must not retain pooled
  hand cards behind or below the result UI.

## Validation

Run `tools/Test-AuraToolsExp.ps1`, `tools/Test-ContentToolSharedBoundary.ps1`,
and both VisualBundle builders when shader or material ownership changes.
