---
name: sunexp-card-art-style
description: Project-local skill for generating, redesigning, replacing, reviewing, or batching Witch's Apocalyptic Journey mod card-face images and relic icons, especially SunExp and GoldExp artwork under ModResource/Images. Use when deriving art from current card/relic names, pack themes, mechanics, accepted references, contact sheets, or user feedback about card art style. Cards use theme-first silhouettes and compact pack palettes; relics use pure-black 32x32-readable centered item silhouettes.
---

# Witch Mod Card And Relic Art Style

Use this skill inside this repository for card-face artwork, relic icons, and
contact-sheet review. Pair it with `imagegen` when generating bitmap assets.
Pair it with `sunexp-mod-dev` when CSV icon paths or validation matter.

## Reference Images

Use bundled references only as style anchors:

- `assets/reference-minimal-eclipse.png`: minimum-detail card style.
- `assets/reference-finisher-collapse.png`: high-rarity finisher card style.
- `assets/reference-relic-morning-shard.png`: approved pure-black relic style.
- `assets/reference-relic-blazing-sundial.png`: complex but readable relic style.

Do not copy these subjects unless the target has the same meaning.

## Choose The Asset Mode

- Card art: symbolic square card-face image under paths such as
  `SunExp/ModResource/Images/Card/SunExp` or
  `GoldExp/ModResource/Images/Card/GoldExp`. Use one centered motif, dark field,
  compact pack palette, and readability at `128x128`.
- Relic icon: physical item icon under paths such as
  `SunExp/ModResource/Images/Relic/SunExp` or
  `GoldExp/ModResource/Images/Relic/GoldExp`. Use pure black background,
  centered object, and readability at `32x32`.

## Workflow

1. Read current names, English names, effects, pack ownership, and icon paths
   from the relevant `SunExp` or `GoldExp` Data/Text CSVs.
2. Derive the visual subject from current mechanics and theme. Ignore old art
   unless the user explicitly asks for an edit.
3. Confirm or define the card-pack palette before final card generation.
4. Generate one image per target. Do not batch distinct cards in one vague
   prompt.
5. Save final images to the exact CSV `Icon` paths unless path changes were
   requested.
6. Create or update a contact sheet outside the game resource folder.
7. Run image and project validation.

## Core Rules

- Cards are readable painted emblems, not full illustrations.
- Card subject colors should stay within 2-3 colors from the pack palette.
- Build cards from theme-specific silhouette first; do not start from generic
  geometry and add style afterward.
- Avoid text, numbers, readable runes, UI frames, scenery, characters, clean
  vector geometry, and tiny ornaments.
- Relics must be centered physical items on pure solid black `#000000`
  backgrounds, including outer-edge pixels.
- Relics have no strict color-count limit, but must stay readable at `32x32`.

## References

- `references/prompt-patterns.md`: card/relic prompt templates, Gold Dream
  palette, and accepted GoldExp subject mappings.
- `references/asset-validation.md`: dimension, mode, black-edge, contact-sheet,
  and CSV path validation expectations.

## Validation

For SunExp assets, run:

```powershell
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

Also verify every replaced card/relic PNG is `512x512` and `RGB` or `RGBA`.
