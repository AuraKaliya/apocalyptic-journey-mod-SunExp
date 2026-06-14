---
name: sunexp-card-art-style
description: >-
  Project-local skill for generating, redesigning, replacing, reviewing, or
  batching Witch's Apocalyptic Journey mod card-face images and relic icons,
  especially SunExp and GoldExp artwork under ModResource/Images. Use when
  deriving art from current card/relic names, pack themes, mechanics, accepted
  references, or user feedback about card art style. Cards use theme-first
  silhouettes, compact pack palettes, reduced details, and brush-stroke symbolic
  composition; relics use pure-black 32x32-readable centered item silhouettes
  with no strict color-count limit.
---

# Witch Mod Card And Relic Art Style

Use this skill inside this repository when the task is about card-face artwork
or relic icons for Witch's Apocalyptic Journey mods. Pair it with `imagegen`
when generating bitmap assets. Pair it with `sunexp-mod-dev` when CSV references
or validation scripts matter.

## Reference Images

Use bundled references only as style anchors:

- `assets/reference-minimal-eclipse.png`: minimum-detail card style.
- `assets/reference-finisher-collapse.png`: high-rarity finisher card style.
- `assets/reference-relic-morning-shard.png`: approved pure-black relic style.
- `assets/reference-relic-blazing-sundial.png`: complex but readable relic style.

Do not copy these subjects unless the target has the same meaning.

## Artwork Modes

Decide whether the target is a card or relic before prompting.

- Card art lives under paths such as
  `SunExp/ModResource/Images/Card/SunExp` or
  `GoldExp/ModResource/Images/Card/GoldExp`. It is a symbolic card-face image
  with a compact pack palette, usually 2-3 subject colors, readable at `128x128`
  or smaller.
- Relic art lives under paths such as
  `SunExp/ModResource/Images/Relic/SunExp` or
  `GoldExp/ModResource/Images/Relic/GoldExp`. It is a physical item icon
  displayed tiny in-game, so it needs a pure black background and a centered
  item silhouette readable at `32x32`.

## Card Style Rules

The target is a readable card icon, not a full illustration.

- Canvas: square PNG, normally `512x512`, RGB or RGBA.
- Background: unified very dark navy / black-purple field. For accepted GoldExp
  gold-dream cards, use a flat single-color field close to `RGB(4,2,48)` with
  no gradient and no scenery.
- Subject: one centered symbolic motif derived from card name, pack theme, and
  effect. Build from theme meaning first, not from a generic geometric icon.
- Palette: choose a compact color system per card pack before generating. Each
  card subject should use only 2-3 colors from that pack palette.
- Shape language: bold theme-first silhouette, thick rough brush strokes,
  painterly massing, torn flame/paint edges, strong dark outline.
- Detail level: avoid tiny ornaments, complex textures, detailed metal,
  scenery, characters, readable text/runes, frames, UI borders, and clean
  vector geometry.
- Readability test: the image should still identify the subject at `128x128`.

## Card Redesign Pipeline

Use this order when a card image looks too geometric, too generic, or not
aligned with the card theme. Do not start from a simple shape and try to add
style afterward.

1. **Theme complexity first**: infer a richer motif from the card name,
   mechanic, and pack fantasy. Add two or three theme-specific components before
   simplifying.
2. **Silhouette compression**: compress those components into one readable
   silhouette. Keep the outer shape distinctive, but readable at `128x128`.
3. **Color restriction**: choose 2-3 subject colors from the pack palette, then
   explicitly forbid extra colors. Use tiny accents only when the mechanic needs
   them.
4. **Detail reduction**: remove small symbols, text-like marks, filigree,
   scenery, and realistic material rendering. Keep large masses and one or two
   identity cues.
5. **Brush-stroke rendering**: ask for expressive painted strokes, rough bristle
   edges, strong dark outlines, and sparse paint flecks.
6. **Contact-sheet judgment**: compare all cards from the pack together. They
   should share background and palette behavior while each card keeps its own
   silhouette.

If the first attempt looks like a geometry icon, regenerate from step 1 with a
more complex theme silhouette. If it looks like a scene, regenerate from step 2
and strengthen "single centered symbolic subject."

## Pack Palette Rules

For cards, design the palette at the card-pack level first, then reuse it.

- Each pack should have `background`, `main`, `shadow`, and optional `accent`.
- Cards from the same pack should look related on a contact sheet.
- Cards from different packs should be distinguishable by color temperature and
  accent behavior.
- If a card pack palette has not been decided yet, pause and discuss it before
  generating final art.

Accepted Gold Dream palette:

- `background`: flat midnight violet near `RGB(4,2,48)`.
- `main`: bright cream gold near `RGB(253,251,200)`.
- `shadow`: ochre gold near `RGB(229,179,64)`.
- `accent`: tiny mint-green accent for false-gold curves, debt magic, or ward
  marks.
- Keep subjects mostly gold. Avoid many-color treasure scenes.

## Card Prompt Template

Use one prompt per card; do not batch distinct cards with one vague prompt.

```text
Generate a 512x512 square card artwork icon.
Card: <Chinese name> / <English name>, <type/rarity>, <pack theme>.
Use this staged design process: first imagine a complex theme-specific motif,
then compress it into a readable silhouette, then reduce it to 2-3 subject
colors, then remove small details and render it as expressive brush strokes.
Very dark navy background, single centered symbolic subject, only 2-3 object
colors total chosen from the card pack palette, no fine details, no text, no
border, no UI frame.
Visual subject: <one richer symbolic motif with 2-3 theme-specific components>.
Pack palette: <background>, <main>, <shadow>, <optional accent>.
Strong compressed silhouette, painterly massing, thick rough brush strokes,
visible bristle edges, readable when tiny.
Avoid gradients, many colors, tiny ornaments, realistic rendering, scenery,
characters, lettering, and clean vector geometry.
```

If output looks too detailed, regenerate with:

```text
Make it more like a simple painted emblem: fewer shapes, fewer highlights,
larger silhouette, less texture, only the chosen pack palette colors on a dark
navy background.
```

If output looks too geometric or generic, regenerate with:

```text
Do not start from simple geometry. Make the silhouette more theme-specific
first: combine <component A>, <component B>, and <component C>, then simplify
that combined silhouette into a rough hand-painted mark. Avoid clean circles,
rectangles, stars, and vector-icon construction.
```

## GoldExp Prompt Pattern

Use this accepted GoldExp card pattern for gold-dream cards:

```text
Use case: stylized-concept
Asset type: square game card art, no frame, no text
Primary request: Recreate the card art for "<Chinese name> / <English name>"
using a staged design process: first imagine a complex <gold/debt/false-dream>
motif, then compress it into a readable silhouette, then reduce it to 2-3
subject colors, then remove small details and render it as expressive brush
strokes.
Scene/backdrop: perfectly flat single-color deep midnight violet background,
RGB 4,2,48. No gradients, no scenery, no texture except sparse brush flecks.
Subject/silhouette: <2-3 theme-specific components>. The silhouette should be
richer than a plain geometric icon, but still readable at small card size.
Style/medium: fantasy roguelike card illustration icon, painterly brush-stroke
mark, strong dark outline, rough bristle edges, hand-painted card icon
readability.
Color limit: subject uses only bright cream gold RGB 253,251,200, ochre gold
RGB 229,179,64, and a tiny mint-green accent. Keep background RGB 4,2,48.
Composition/framing: centered single subject with generous padding, no UI
border.
Constraints: no text, no numbers, no watermark, no card frame, avoid clean
vector geometry, avoid realistic rendering, avoid too many colors.
```

Accepted GoldExp subject mappings:

- `镀金护符`: cracked gold coin ring + hanging contract tag + central ward flame.
- `金梦押注`: overlapping false-gold coins + betting crescent + torn contract chip.
- `乾坤一掷`: diagonal thrown-coin comet + cracked fragments + trailing contract ribbon.
- `赝金雨`: arcing rain of uneven fake coins + mint splash + dissolving gold dust.
- `空头支票`: torn golden blank check/contract + diagonal slash + false-gold chips.
- `黄金时代`: old false-gold coin + radiant crown-sunburst + broken coin rays.

## Relic Style Rules

The target is a readable item icon, not a card face and not a scene.

- Canvas: final project PNG must be square `512x512`, RGB or RGBA.
- Background: pure solid black `#000000`. Verify all outer-edge pixels are black.
- Subject: one centered physical object derived from the relic name, tips, pack
  theme, and effect.
- Silhouette: prioritize a strong outer shape and one or two unmistakable item
  features. It must still be identifiable at `32x32`.
- Composition: keep stable black padding; the object usually occupies 60-75% of
  the canvas.
- Palette: no strict color-count limit. Use enough color/value contrast to show
  the object, but avoid visual noise.
- Detail level: broad facets, cuts, folds, cracks, and brush marks are fine;
  avoid labels, numerals, readable runes, dense filigree, many sparks, complex
  mechanisms, or scene backgrounds.

## Relic Prompt Template

```text
Generate a simplified 512x512 square game relic icon.
Relic: <Chinese name> / <English name>, <rarity>, <pack theme>.
Pure solid black #000000 background, one centered physical item, strong
silhouette, readable at 32x32, no text, no numbers, no runes, no border,
no UI frame, no scenery, no character.
Visual subject: <one clear item based on the relic name/tips/effect>.
Theme accents: <pack-specific accents, not a strict 2-3 color limit>.
Large readable object shapes, broad painted facets/details, generous black
padding, item occupies about 60-75% of the canvas.
Avoid tiny ornaments, labels, dense sparks, complex mechanisms, realistic scene
rendering, background glow, and edge cropping.
```

## Card Workflow

1. Read current names and icon paths from the relevant mod CSVs, for example:
   - `SunExp/Text/Card/sunexp.csv`
   - `SunExp/Data/Card/sunexp.csv`
   - `GoldExp/Text/Card/goldexp.csv`
   - `GoldExp/Data/Card/goldexp.csv`
2. Derive each image from current name, type, pack theme, and effect. Ignore
   old image content unless explicitly asked to edit it.
3. Confirm or define the palette for each involved card pack. For GoldExp,
   default to the accepted Gold Dream palette above.
4. Generate one image per card with `imagegen`.
5. Save final card images to the exact paths referenced by `Icon` in the
   relevant `Data/Card/*.csv`.
6. Create or update a contact sheet outside the game resource folder, for
   example:
   - `tools/previews/sunexp_card_redesign_contact_sheet.png`
   - `tools/previews/goldexp-card-icons-generated-remake.png`
7. Validate asset paths and dimensions before finishing. For SunExp, run:

```powershell
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

Also verify every card PNG is `512x512` and `RGB` or `RGBA` when replacing
final project assets.

## Relic Workflow

1. Read current relic names, tips, pack ownership, and icon paths from the
   relevant `Text/Relic/*.csv` and `Data/Relic/*.csv`.
2. Derive each relic from current name, English name, tips, rarity, pack theme,
   and effect.
3. Generate or reuse approved preview images with `imagegen`, one prompt per
   relic.
4. Normalize generated images to `512x512` PNG.
5. Fit each object with black padding and verify edge pixels are pure
   `#000000`. Produce `32x32` thumbnails and an enlarged contact sheet before
   final replacement.
6. Save final relic images to exact paths referenced by `Icon` in
   `Data/Relic/*.csv`.
7. Run project validation where available and report any unrelated CSV failures
   separately from targeted relic path checks.

## Acceptance Checklists

Card acceptance:

- Every card is recognizable from its own silhouette.
- Each pack reads as its own color family on the contact sheet.
- Subject colors are constrained to 2-3 colors from the pack palette.
- The art feels theme-specific before it feels simplified.
- No text, border, UI frame, character scene, detailed background, or clean
  vector geometry appears.
- Card files match CSV `Icon` paths unless path changes were requested.

Relic acceptance:

- Every relic reads as a centered physical item, not a card scene or abstract
  effect background.
- Every relic remains recognizable at `32x32`.
- Background is pure black `#000000`, including all outer-edge pixels.
- No text, label, numbers, readable runes, UI frame, decorative border,
  character, landscape, or scenery appears.
- Relic files match CSV `Icon` paths unless path changes were requested.
