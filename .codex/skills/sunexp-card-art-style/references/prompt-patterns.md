# Prompt Patterns

Use one prompt per card or relic.

## Card Prompt

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

If output looks too detailed, ask for fewer shapes, larger silhouette, less
texture, and only chosen palette colors. If it looks too generic, combine two
or three theme-specific components before simplifying.

## Relic Prompt

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

## Gold Dream Palette

- `background`: flat midnight violet near `RGB(4,2,48)`.
- `main`: bright cream gold near `RGB(253,251,200)`.
- `shadow`: ochre gold near `RGB(229,179,64)`.
- `accent`: tiny mint-green accent for false-gold curves, debt magic, or ward
  marks.

Keep GoldExp gold-dream card subjects mostly gold. Avoid many-color treasure
scenes, gradients, scenery, or frame-like borders.

## Accepted GoldExp Subject Mappings

- `镀金护符`: cracked gold coin ring + hanging contract tag + central ward flame.
- `金梦押注`: overlapping false-gold coins + betting crescent + torn contract chip.
- `乾坤一掷`: diagonal thrown-coin comet + cracked fragments + trailing contract ribbon.
- `赌金雨`: arcing rain of uneven fake coins + mint splash + dissolving gold dust.
- `空头支票`: torn golden blank check/contract + diagonal slash + false-gold chips.
- `黄金时代`: old false-gold coin + radiant crown-sunburst + broken coin rays.
