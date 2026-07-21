# Card Visual Skins And Effects

Use this reference for card face skins, card frames, card-frame material
effects, and card-use visual effects.

## Runtime Split

- `GameApi/CardVisualSkinApi.cs` and `GameApi/CardVisualEffectApi.cs` expose
  registration and CSV-facing facades.
- `Mechanics/CardVisualSkin*` and `Mechanics/CardVisualEffect*` own matching,
  priority, target, and owner-scoped registry behavior.
- `Hooks/CardVisualSkinRuntime.cs` observes card UI lifecycle and applies
  registered rules.
- `Hooks/Visual/CardVisualSkinApplier.cs`,
  `CardVisualSkinMarker.cs`, `CardVisualSkinSpriteCache.cs`,
  `CardVisualEffectApplier.cs`, `CardFrameEffectApplier.cs`, and
  `CardFrameEffectMaterials.cs` mutate Unity card visuals.
- `Mechanics/RuntimeCardAttachmentService.cs` owns runtime-added card
  attachments such as special generated cards.

## Rules

- Register rules by stable owner identity and stable ids. Do not rely on
  localized card names for matching.
- Prefer pack ids, full card ids, icon prefixes, or explicit registry targets
  over string searches in UI hierarchies.
- Keep conflict resolution priority-driven and deterministic.
- Clear owner-scoped registrations when replacing a rule set in tests or
  initialization.
- Keep visual mutation in hook/visual appliers. Do not add Unity object mutation
  to CSV-callable `Scripting`.
- Keep card art generation in `terrias-card-art-style`; keep runtime skin/effect
  application here.
- For dynamic card-frame effects that must survive native card UI lifecycle
  events, especially card-use or Burnout destruction animations, prefer an
  integrated dynamic frame-skin material on the real frame node. Independent
  overlay objects can remain visually detached from native animation and may
  linger or pop during burn/destroy transitions.
- Use card-frame overlays only as a fallback when the runtime card UI lacks a
  real frame `Image` or `MeshRenderer` target and a resolved frame sprite or
  texture is available. Fallback overlays must stay non-blocking and below text,
  but should not be the primary battle-card path.
- Integrated card-frame materials should use the resolved frame texture as
  `_MainTex`, render in non-overlay shader mode, and disable overlay-only
  frame masking. Clear paths must restore original frame materials because card
  UI instances may be pooled and reused.
- When debugging mismatches between Unity Lab, dictionary cards, and battle
  cards, first determine whether the card surface is a true frame node or a
  fallback/background-sized overlay. Tune parameters only after the rendering
  path and shader mode match the intended surface.

## Validation

`tools\Test-TerriasCSharp.ps1` contains focused registry behavior tests for card
visual skin and effect resolution. Update those tests when changing matching,
priority, or owner-clear semantics.

`tools\Test-TerriasArchitecture.ps1` should guard fragile visual lifecycle
decisions, including whether card-frame effects use integrated frame materials
or fallback overlays.
