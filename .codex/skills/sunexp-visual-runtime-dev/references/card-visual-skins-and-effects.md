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
- Keep card art generation in `sunexp-card-art-style`; keep runtime skin/effect
  application here.

## Validation

`tools\Test-SunExpCSharp.ps1` contains focused registry behavior tests for card
visual skin and effect resolution. Update those tests when changing matching,
priority, or owner-clear semantics.
