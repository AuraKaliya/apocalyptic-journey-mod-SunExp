---
name: sunexp-card-art-style
description: >-
  Project-local skill for generating or redesigning SunExp card-face images and
  relic icons in an official-like Witch's Apocalyptic Journey style. Use when
  drawing, regenerating, replacing, reviewing, or batching SunExp artwork under
  SunExp/ModResource/Images/Card/SunExp or
  SunExp/ModResource/Images/Relic/SunExp, especially when deriving art from
  current card/relic names, pack themes, and effects. Distinguishes card art
  from relic icons: cards use compact pack palettes and symbolic card-face
  composition; relics use pure-black 32x32-readable centered item silhouettes
  with no strict color-count limit.
---

# SunExp Card And Relic Art Style

Use this skill inside this repository when the task is about SunExp card-face artwork or relic icons. Pair it with `sunexp-mod-dev` when Data/Text CSV references or validation matter, and with `imagegen` when generating bitmap assets.

## Reference Images

Use the bundled references only as style anchors:

- `assets/reference-minimal-eclipse.png`: best example of the target minimum-detail style.
- `assets/reference-finisher-collapse.png`: example for high-rarity finisher cards that still need to stay icon-like.
- `assets/reference-relic-morning-shard.png`: approved relic example for a pure-black, centered item silhouette.
- `assets/reference-relic-blazing-sundial.png`: approved relic example for a more complex item that still reads at `32x32`.

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

## Artwork Modes

Decide whether the target is a card or a relic before prompting. Do not apply
card constraints blindly to relics.

- Card art lives under `SunExp/ModResource/Images/Card/SunExp`. It is a card
  face image: a symbolic object or effect expression with a compact pack
  palette, usually only 2-3 object colors, readable at `128x128` or smaller.
- Relic art lives under `SunExp/ModResource/Images/Relic/SunExp`. It is a
  physical item icon that the game displays as a `32x32` relic. It needs a pure
  black background, a centered object silhouette, and obvious item identity at
  tiny size. Relics are not bound to the card 2-3 color rule.

## Card Style Rules

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

## Relic Style Rules

The target is a readable item icon, not a card face and not a scene.

- Canvas: final project PNG must be square `512x512`, RGB or RGBA. The
  generated source may be any square size, but normalize before replacing
  assets.
- Background: pure solid black `#000000`. No navy field, texture, vignette,
  pattern, glow wash, frame, or UI border. Verify all outer-edge pixels are
  black after fitting.
- Subject: one centered physical object derived from the current relic name,
  tips, pack theme, and effect. It should read as an item: shard, mirror,
  bottle, dial, wheel, crown-heart, prism, throne, charm, sundial, belt, etc.
- Silhouette: prioritize a strong outer shape and one or two unmistakable item
  features. The icon must still be identifiable when downscaled to `32x32`.
- Composition: keep the object in the center with stable black padding,
  normally occupying about 60-75% of the canvas. Do not crop flames, arcs,
  handles, or crowns at the edge.
- Palette: no strict color-count limit. Use enough color and value contrast to
  show the object, but avoid visual noise. For the current solar relic set,
  prefer gold, pale yellow, amber, ember red, charcoal, and small white-hot
  highlights.
- Detail level: broad facets, cuts, folds, cracks, and brush marks are fine;
  tiny ornaments, labels, numerals, readable runes, dense filigree, many sparks,
  detailed gears, or scene backgrounds are not.

## Pack Palette Rules

For cards, design the palette at the card-pack level first, then reuse it
consistently inside that pack.

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

For relics, use pack theme as subject and accent guidance rather than as a hard
palette lock:

- `Radiance: Spark` / `日耀：星火`: dawn fragments, solar cores, prisms,
  mirrors, phase dials, ignition and origin objects.
- `Radiance: Ember Crown` / `日耀：烬冠`: charred cloth, ember amulets,
  gathered inward flame, crown heat, protective wheels, fire-worn materials.
- `Radiance: Canopy` / `日耀：天幕`: contained suns, low sky arcs, oppressive
  sundials, calamity wind rings, compressed or spreading solar pressure.

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

## Card Prompt Template

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

## Relic Prompt Template

Use one prompt per relic. Mention the approved relic standard explicitly.

```text
Generate a simplified 512x512 square game relic icon.
Relic: <Chinese name> / <English name>, <rarity>, <pack theme>.
Pure solid black #000000 background, one centered physical item, strong
silhouette, readable at 32x32, no text, no numbers, no runes, no border,
no UI frame, no scenery, no character.
Visual subject: <one clear item based on the relic name/tips/effect>.
Theme accents: <pack-specific solar accents, not a strict 2-3 color limit>.
Large readable object shapes, broad painted facets/details, generous black
padding, item occupies about 60-75% of the canvas.
Avoid tiny ornaments, labels, dense sparks, complex mechanisms, realistic
scene rendering, background glow, and edge cropping.
```

If a relic output looks like a card, scene, or badge, regenerate with stronger
wording:

```text
Make it more like a single physical item icon on pure black: simpler outer
silhouette, fewer small marks, more black padding, clearer object identity at
32x32, no scene and no symbolic background.
```

## Card Workflow

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

## Relic Workflow

1. Read current relic names, tips, pack ownership, and icon paths from:
   - `SunExp/Text/Relic/sunexp.csv`
   - `SunExp/Data/Relic/sunexp.csv`
   - `SunExp/Text/CardPack/sunexp.csv`
2. Derive each relic from the current name, English name, tips, rarity, pack
   theme, and effect. Ignore old art unless explicitly asked to edit it.
3. Generate or reuse approved preview images with `imagegen`, one prompt per
   relic. Keep approved samples as anchors when the user has already accepted a
   visual standard.
4. Normalize generated images to `512x512` PNG. The image generator may ignore
   requested dimensions.
5. Fit each object with black padding and verify edge pixels are pure
   `#000000`. Produce `32x32` thumbnails and an enlarged contact sheet before
   final replacement.
6. Save final relic images to the exact paths referenced by `Icon` in
   `Data/Relic/sunexp.csv`, normally:
   - `SunExp/ModResource/Images/Relic/SunExp/<id>.png`
7. Create or update a contact sheet outside the game resource folder, for
   example:
   - `tools/previews/relic_redesign/full_redraw/sunexp_relic_full_redraw_contact_sheet.png`
8. Run validation before finishing:

```powershell
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

Also verify every relic PNG is `512x512`, `RGB` or `RGBA`, and has pure black
outer edges. If broad validation fails on unrelated CSV description/header
rows, still run and report a targeted relic icon path check for all real relic
rows.

## Card Acceptance Checklist

- Every card is recognizable from its own silhouette.
- Each pack reads as its own color family on the contact sheet.
- Object colors are constrained to 2-3 colors from the pack palette.
- Cross-pack cards are distinguishable by palette without relying on text.
- No text, border, UI frame, character scene, or detailed background appears.
- Card files match the CSV `Icon` paths without CSV edits unless the user requested path changes.

## Relic Acceptance Checklist

- Every relic reads as a centered physical item, not a card scene or abstract
  effect background.
- Every relic remains recognizable at `32x32` from silhouette plus one or two
  strong item features.
- Background is pure black `#000000`, including all outer-edge pixels.
- Color and detail are richer than cards when useful, but still controlled
  enough that tiny-size readability is not lost.
- No text, label, numbers, readable runes, UI frame, decorative border,
  character, landscape, or scenery appears.
- Relic files match the CSV `Icon` paths without CSV edits unless the user
  requested path changes.
