---
name: sunexp-card-art-style
description: Project-local skill for generating or redesigning SunExp card-face images in the official-like Witch's Apocalyptic Journey card icon style. Use when drawing, regenerating, replacing, reviewing, or batching SunExp card artwork under SunExp/ModResource/Images/Card/SunExp, especially when aligning card art to a unified dark-background, 2-3 color, bold-symbol style based on card names and pack themes.
---

# SunExp Card Art Style

Use this skill inside this repository when the task is about SunExp card-face artwork. Pair it with `sunexp-mod-dev` when Data/Text CSV references or validation matter, and with `imagegen` when generating bitmap assets.

## Reference Images

Use the bundled references only as style anchors:

- `assets/reference-minimal-eclipse.png`: best example of the target minimum-detail style.
- `assets/reference-finisher-collapse.png`: example for high-rarity finisher cards that still need to stay icon-like.

Do not copy these subjects unless the target card has the same meaning.

Official card references should be read as series-style examples, not as a
single universal palette:

- Time / stasis style cards use a dark field with pale mint, cyan-teal, and
  desaturated blue-green objects.
- Blood / flow style cards use a dark field with bone yellow, crimson, magenta,
  and deep violet objects.
- Spell-material / star style cards use a dark field with icy cyan, periwinkle,
  pale lavender, and small white star accents.

The important lesson is that each card series has its own small color system.
Do not force every SunExp card into the same ivory / magenta / crimson palette.

## Style Rules

The target is a readable card icon, not a full illustration.

- Canvas: square PNG, normally `512x512`, RGB or RGBA.
- Background: unified very dark navy / black-purple field.
- Subject: one centered symbolic object derived from the card name, pack theme, and effect.
- Palette: choose a compact color system per card pack before generating art.
  Individual card objects should use only 2-3 colors from that pack palette.
  Do not use a global palette for all packs.
- Shape language: large flat shapes, bold silhouette, thick rough brush strokes, torn flame edges.
- Detail level: avoid tiny ornaments, complex textures, detailed metal, scenery, characters, text, frames, UI borders, and readable symbols/runes.
- Readability test: the image should still identify the subject at `128x128` or smaller.

## Pack Palette Rules

Design the palette at the card-pack level first, then reuse it consistently
inside that pack.

- Each pack should have:
  - `background`: normally the shared near-black navy / black-purple field.
  - `main`: the dominant readable object color.
  - `shadow`: the secondary mass color for cuts, smoke, flame, cracks, or motion.
  - `accent`: an optional tiny highlight color used sparingly.
- Cards from the same pack should look related on a contact sheet even when the
  silhouettes differ.
- Cards from different packs should be distinguishable at a glance by color
  temperature and accent behavior.
- A card may borrow a tiny accent from another pack only when its mechanical
  effect explicitly crosses into that pack's theme. The card's own pack palette
  should still dominate.
- If a card pack's palette has not been decided yet, pause and discuss the
  palette before generating final art.

## Theme Mapping

Infer the subject fresh from the current card name and pack. Do not rely on old art if the name changed.

- `Radiance: Spark` / `日耀：星火`: ignition, oath, sun disk, phase dial, return arc, morning shield, origin core.
- `Radiance: Ember Crown` / `日耀：烬冠`: gathered flame, ember tower, flame cycle, crown oath, burning star, collapse, self-burn pressure.
- `Radiance: Canopy` / `日耀：天幕`: burning sky arc, spreading calamity, eclipse, smoke erosion, impurity purge, enemy burn/debuff spread.

Palette choices for these packs are project decisions. Do not assume the old
ivory / magenta / crimson palette is correct for all three.

Rarity can guide intensity:

- Rarity 1: simplest single symbol.
- Rarity 2: one symbol plus one clear motion or aura.
- Rarity 3 / finisher: larger silhouette and stronger motion, but still no scene or fine debris.

## Prompt Template

Use one prompt per card; do not batch distinct cards with a single vague prompt.

```text
Generate a simplified 512x512 square card artwork icon.
Card: <Chinese name> / <English name>, <type/rarity>, <pack theme>.
Very dark navy background, single centered symbol, only 2-3 object colors total
chosen from the card pack palette, no fine details, no text, no border, no UI frame.
Visual subject: <one clear symbolic object based on the name/effect>.
Pack palette: <background>, <main>, <shadow>, <optional accent>.
Large flat shapes, thick rough brush strokes, clear silhouette, readable when tiny.
Avoid gradients, many colors, tiny ornaments, realistic rendering, scenery, characters, lettering.
```

If the output looks too detailed, regenerate with stronger wording:

```text
Make it more like a simple painted emblem: fewer shapes, fewer highlights,
larger silhouette, less texture, only the chosen pack palette colors on a dark navy background.
```

## Workflow

1. Read current card names and icon paths from:
   - `SunExp/Text/Card/sunexp.csv`
   - `SunExp/Data/Card/sunexp.csv`
   - `SunExp/Text/CardPack/sunexp.csv`
2. Derive each image from the current card name, type, pack theme, and effect. Ignore prior image content unless explicitly asked to edit it.
3. Confirm or define the palette for each involved card pack before generating final art.
4. Generate preview images with `imagegen`.
5. Before replacing project assets, normalize generated images to `512x512` PNG. The image generator may ignore requested dimensions.
6. Save final card images to the exact paths referenced by `Icon` in `Data/Card/sunexp.csv`.
7. If tracked atlas/source preview images exist in `SunExp/ModResource/Images/Card/SunExp`, update them so they do not preserve stale art direction.
8. Create or update a contact sheet outside the game resource folder, for example:
   - `tools/previews/sunexp_card_redesign_contact_sheet.png`
9. Run validation before finishing:

```powershell
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

Also verify every card PNG is `512x512` and `RGB` or `RGBA`.

## Acceptance Checklist

- Every card is recognizable from its own silhouette.
- Each pack reads as its own color family on the contact sheet.
- Object colors are constrained to 2-3 colors from the pack palette.
- Cross-pack cards are distinguishable by palette without relying on text.
- No text, border, UI frame, character scene, or detailed background appears.
- Card files match the CSV `Icon` paths without CSV edits unless the user requested path changes.
