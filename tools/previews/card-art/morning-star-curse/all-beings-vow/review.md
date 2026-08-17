# 晨星：诅咒 / 众生愿

- Review status: passed internal series validation
- Approved series benchmark: `../morning-star-elegy/04-final-256.png`
- Final asset: `04-final-256.png`
- Built-in image generation mode: `image_gen`
- Final SHA-256: `C86670648E0BB0DB796B319EF367E981C7450C63C0FDDFD12421AD23DF014111`

## Reverse Analysis

- Subject and visual theme: Exactly seven independent deep-ultramarine and smoke-violet prayer branches bend toward one off-center three-petal old-gold/ivory wish-flame. Each unfinished branch ends in one restrained red tip.
- Pack palette and approximate color-area ratios: Deep ultramarine and smoke violet carry the seven sources; wine red and crimson are limited to their final directional tips; old-gold/ivory forms the single answered wish. The final canvas contains 73.99% exact black.
- Dominant painted masses: Six staggered lower branches, one isolated upper-left short branch, and one compact three-petal wish-flame.
- Secondary painted masses: Seven red arrival tips belong to their branches rather than acting as separate particles or counters.
- Characteristic silhouette features: Unequal S-curves, different widths, staggered heights, one low hooked isolated branch, and one off-center warm destination make the source count readable without a radial arrangement.
- Black negative-space structure: Black separates all seven branches along their full lengths, opens between the three gold petals, and surrounds the entire subject with a continuous margin.
- Brush size, density, direction, and edge character: Each branch uses one thick coarse serpentine stroke; shorter red strokes sharpen the final direction; three broad gold strokes form the focal wish.
- Detail density and focal point: The branches remain texture-light and countable. Detail and value peak only at the upper-right wish-flame.
- What creates the visual impact: Seven individually readable wishes share one direction but not one origin, while the isolated short branch breaks the potential picket-row rhythm.
- Geometry or simplification risks: Repeated branches could become fingers, a fan, beams, or a vote chart. Unequal curves, different lengths, staggered placement, organic tapers, and the isolated seventh source prevent a regular symbol.
- Features the series must inherit: Exact-black gaps, five-color hierarchy, coarse organic motion, mechanism-bearing counts, and restrained old-gold reward light.
- Features later images must not mechanically copy: The six-plus-one branch layout, three-petal destination, serpentine vertical motion, or isolated upper-left source.

## Hard Gates

- PASS: final image is square and exactly `256x256`.
- PASS: final outer border contains zero non-black pixels; the final contains 48,492 exact `#000000` pixels (73.99%).
- N/A: this target is a card, not a relic.
- PASS: no frame, UI, text, watermark, people, hands, candle, energy ball, prayer circle, fan, beam matrix, or vector geometry appears.
- PASS: seven independent sources and one off-center destination remain readable at final size.
- PASS: the subject is not constructed from circles, straight lines, regular polygons, rings, or equal radial geometry.
- PASS: stage 2 uses five visually distinct foreground groups.
- PASS: stage 3 and final retain the same five groups without increasing their count.
- PASS: the final foreground bounding region is approximately 68.8% wide by 77.0% high.
- PASS: exactly seven branches and exactly three connected wish-flame petals remain distinguishable.

## Scoring

| Dimension | Score | Notes |
| --- | ---: | --- |
| Palette consistency | 20/25 | All five groups remain visible; crimson is deliberately confined to short unresolved tips while the enlarged gold flame supplies the warm focal mass. |
| Complexity consistency | 22/25 | Seven source strokes reach the count ceiling while internal detail stays lower than the `众生相` contours. |
| Silhouette recognition | 26/30 | The seven branches remain countable at 256px, with the isolated short branch breaking repetition. |
| Visual effect | 18/20 | The off-center warm answer gives the many-source motion a clear destination and finished card-face presence. |
| **Total** | **86/100** | Passes the series threshold. |

## Generation Record

1. Stage 1 generated multiple differentiated prayer branches approaching one warm organic wish-flame.
2. Stage 2 edited away botanical clutter, corrected the source count to seven, compressed the palette, and reduced the destination to three connected fire-petals.
3. Stage 3 edited every branch into a coarse mass and removed leaf-like tip clutter. Framing/density revisions compacted and thickened the branches; an isolated upper-left source restored an unambiguous seventh count.
4. A palette correction shortened every red unresolved tip and enlarged the three-petal gold destination. `tools/card_art_finalize.py` then keyed the green background to exact black, fitted the subject to series bounds, and proportionally downsampled to `256x256`.

## Decision

Passes the approved-series hard gates and scoring threshold. Keep as the final preview for `众生愿`; installation into shipped mod resources remains separate.
