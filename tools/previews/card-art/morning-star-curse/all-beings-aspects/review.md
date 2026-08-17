# 晨星：诅咒 / 众生相

- Review status: passed internal series validation
- Approved series benchmark: `../morning-star-elegy/04-final-256.png`
- Final asset: `04-final-256.png`
- Built-in image generation mode: `image_gen`
- Final SHA-256: `5938FE14C9DDABE21AAD3453EC5B4B30A78277D769529148024A916EF12675DF`

## Reverse Analysis

- Subject and visual theme: One deep-ultramarine living root carries exactly seven naturally staggered hood/concave petals. Six remain smoke violet; one off-center upper petal opens old-gold/ivory and retains one crimson interior mark.
- Pack palette and approximate color-area ratios: Deep ultramarine forms the shared root and is the largest group; smoke violet describes the six alternatives; wine red and crimson mark each attachment and the chosen interior; old-gold/ivory is confined to the selected petal. The final canvas contains 74.01% exact black.
- Dominant painted masses: One curved root trunk, three uneven branch tiers, and one selected warm petal.
- Secondary painted masses: Exactly seven distinct petal silhouettes—the series complexity ceiling—with no extra blessing counters.
- Characteristic silhouette features: Crescent, serrated bowl, split hood, curled tongue, drooping bell, clawed canopy, and torn gold leaf make every option readable from contour alone.
- Black negative-space structure: Black separates every petal, opens each concavity, cuts between branch tiers, and leaves a continuous outer margin.
- Brush size, density, direction, and edge character: Broad root strokes rise and fork; each petal uses one or two coarse enclosing strokes; red attachment strokes point inward; the chosen gold petal uses one warm upward stroke.
- Detail density and focal point: The root stays quiet while contour variety carries information. The gold petal above center is the only high-value focal mass.
- What creates the visual impact: Seven visibly different forms share one origin, while one warm opening expresses random selection without a menu or human portrait.
- Geometry or simplification risks: Branch tiers could become a selection tree or grid. Unequal contours, unequal heights, curved joins, nonuniform spacing, and one off-axis selected petal keep the form botanical rather than UI-like.
- Features the series must inherit: Exact-black separation, five-color hierarchy, coarse organic silhouettes, countable mechanism objects, and restrained warm benefit light.
- Features later images must not mechanically copy: The seven-petal tree structure, selected top petal, three-tier branch layout, or any individual petal contour.

## Hard Gates

- PASS: final image is square and exactly `256x256`.
- PASS: final outer border contains zero non-black pixels; the final contains 48,505 exact `#000000` pixels (74.01%).
- N/A: this target is a card, not a relic.
- PASS: no frame, UI, text, watermark, portrait, literal face, tarot spread, menu, grid, radial ring, or vector geometry appears.
- PASS: the shared organic root and seven distinct blessing forms remain readable at final size.
- PASS: the subject is not constructed from circles, straight lines, regular polygons, rings, or equal repeated geometry.
- PASS: stage 2 uses five visually distinct foreground groups.
- PASS: stage 3 and final retain the same five groups without increasing their count.
- PASS: the final foreground bounding region is approximately 77.3% wide by 77.0% high.
- PASS: exactly seven total petals remain distinguishable, with exactly one selected gold petal.

## Scoring

| Dimension | Score | Notes |
| --- | ---: | --- |
| Palette consistency | 20/25 | All five groups remain present; the gold area is intentionally smaller than the nominal series target because only one of seven petals is selected. |
| Complexity consistency | 23/25 | Seven countable petals reach the allowed ceiling while the root and internal detail stay coarse. |
| Silhouette recognition | 27/30 | Every blessing option remains distinguishable by outer contour at 256px. |
| Visual effect | 18/20 | The single warm reveal reads quickly without overwhelming the plurality of violet forms. |
| **Total** | **88/100** | Passes the series threshold. |

## Generation Record

1. Stage 1 generated one living root and seven distinct hood-like petals, including one selected gold petal.
2. Stage 2 edited the image against the approved benchmark, compressed it to five groups, merged surface detail, retained exactly seven petals, and added restrained red attachment shadows.
3. Stage 3 edited the root and petals into coarse masses while preserving every contour and black separation.
4. Two framing corrections compacted the overly tall growth and widened the existing branches without changing the seven-petal count. A chroma-key source then allowed `tools/card_art_finalize.py` to composite exact black and downsample proportionally to `256x256`.

## Decision

Passes the approved-series hard gates and scoring threshold. Keep as the final preview for `众生相`; installation into shipped mod resources remains separate.
