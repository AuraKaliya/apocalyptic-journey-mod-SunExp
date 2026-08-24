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

The shipped Terrias defaults keep frame skins and dynamic effects as two
independent maps:

- the solar frame preset expands the entire
  `Terrias_terrias_cardpack_solar_ember_crown_canopy` pack;
- the morning-star frame preset expands the entire
  `Terrias_terrias_cardpack_morning_star_overture` pack and explicitly keeps
  the four generated `Terrias_terrias_stellar_overture_*` cards in that frame
  theme even when they are outside the native pack catalog;
- `foil-holo` applies only to `Terrias_terrias_blazing_crown_collapse`;
- `stardust` applies only to the four
  `Terrias_terrias_stellar_overture_*` cards.

Never infer the dynamic-effect map from a frame theme or reduce the full-pack
frame preset to the five cards that carry effects.

## Runtime Rules

- Keep resource ids and card ids stable and owner-qualified.
- Restore original sprites, textures, and materials when disabling a mapping or
  reusing a pooled card view.
- Keep visual-only overlays non-blocking for raycasts and directly adjacent to
  their source frame/face layer.
- A combat presentation callback is valid only when one candidate owns the
  exact visual root, `IDataConfig`, and `ICard`. Never combine a root observed
  on one callback/object with config or card data from another. Reject stale
  pooled views when instance identities disagree.
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
  reset; bind emits a shared apply. Temporary materials form an ownership
  stack: native material, Aura card effect, then card-exit animation. Pooling,
  teardown, and destruction must unwind that stack in reverse order. A material
  owner may restore or release only while its exact material still owns the
  renderer; it must never reattach a stale or destroyed material over a newer
  owner. Card-visual protocol v9 uses the `frame`
  target with `native-frame-v5`: select the whole native presentation mode
  exactly as `ICard.SetCardStyle` does. If `Front/background` owns a
  `MeshRenderer`, mutate only the matching `Front/FrontBack.MeshRenderer`; a
  coexisting legacy `Image` must remain untouched. Otherwise mutate only the
  matching `Image`. Static skins, dynamic effects, material leases, and reset
  all share this one selection. A combat card root never performs descendant-wide
  searches; a non-combat root may resolve only its deterministic direct
  `CardItem` child. Do not restore the retired detached `frameOverlay`, white
  full-card `cardFront` surface, or breadth-first fallback; they respectively
  break native UV/masks or can skin a neighboring pooled card.
- `aura.card-visual.material-v7` has two explicit URP material contracts. Unity UI
  `Image` uses `AuraToolsExp/Materials/CardFrameEffectUI`; `MeshRenderer` uses
  `AuraToolsExp/Materials/CardFrameEffectURP`. Both shaders must declare
  `RenderPipeline=UniversalPipeline`, expose their required named pass, and
  resolve under an active scriptable render pipeline. Missing, incompatible,
  unsupported, or zero-pass shaders fail closed instead of producing a purple
  fallback material or silently darkening the card. The UI shader has no
  built-in `UI/Default` fallback.
- Build `auratools_visuals` with Unity `6000.0.46f1`, StandaloneWindows64, and
  URP `17.0.4`. The build project must bind a real URP Pipeline Asset before
  `BuildAssetBundles`; otherwise Unity's scriptable stripper can serialize the
  URP shader with zero D3D11 programs even when import reported no errors. The
  builder must fail on shader compiler errors, zero compiled D3D11 programs,
  missing post-build materials, wrong pipeline/pass tags, and package both
  materials. Building under `-nographics` is insufficient: use Direct3D11 and
  render the bundled UI material through a real ScreenSpaceCamera Canvas into
  a RenderTexture, then reject magenta or empty readback pixels. Never retain
  or ship the retired Unity 2022 bundle after a failed rebuild.
- Replay card instances carry a read-only snapshot of their effective theme,
  skin, effect id, and clamped parameter map in runtime Vars. Applying that
  snapshot uses the same v9/native-frame-v5 renderer and never rewrites current player config.
- A pooled card exit leaves the live hand hierarchy before its animation. Burn,
  discard, and draw-pile exits all repair sibling/sorting indexes and coalesce
  one native hand layout; do not limit reflow to move exits while burn suppresses
  the native `cardcontainer` callback.
- Outcome entry, battle settling, restart, and battle end all perform an
  idempotent hand-view teardown. A settlement screen must not retain pooled
  hand cards behind or below the result UI.

## Validation

Run `tools/Test-AuraToolsExp.ps1`, `tools/Test-ContentToolSharedBoundary.ps1`,
and both VisualBundle builders when shader or material ownership changes.
