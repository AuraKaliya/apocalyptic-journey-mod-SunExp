# 晨星：诅咒 / 晨星：回光

- Review status: passed internal series validation
- Approved series benchmark: `../morning-star-elegy/04-final-256.png`
- Final asset: `04-final-256.png`
- Built-in image generation mode: `image_gen`
- Final SHA-256: `53704F44A072C0C07F51862373AD897AA68D0011F9FBCC91BCFE230968096ED7`

## Reverse Analysis

- Subject and visual theme: Three unequal deep-ultramarine mantles enter one below-center rupture from the left, lower edge, and right. Smoke-violet Curse shells peel at the join, and four old-gold/ivory petals return toward the upper left.
- Pack palette and approximate color-area ratios: Deep ultramarine is the largest dark structure; smoke violet is restricted to peeling shells; wine red and crimson create the shared rupture; old-gold/ivory forms the four returned petals. The final canvas contains 72.57% exact black.
- Dominant painted masses: One broad hooked left mantle, one narrow folded lower mantle, one shorter torn right mantle, and one compact red join.
- Secondary painted masses: Four uneven violet shell fragments and four connected return petals, with the fragments visually subordinated to the main action.
- Characteristic silhouette features: The left crescent, clipped right hook, downward blue tail, and high four-petal return keep the convergence asymmetric and organic.
- Black negative-space structure: Black separates all three incoming sources before the join, cuts between the four gold petals, and leaves a continuous outer margin.
- Brush size, density, direction, and edge character: Long hooked blue strokes move inward; short torn violet strokes peel outward; thick red strokes bind the rupture; broad dry gold strokes reverse upward-left.
- Detail density and focal point: The focal density sits at the convergence point. The source mantles and return petals remain large and simple.
- What creates the visual impact: Three dark flows visibly collapse into one wound and rebound as one warm answer, communicating total-zone cleanup without showing card piles.
- Geometry or simplification risks: Three sources could become an equal triad or recycle symbol, and gold could become a sunburst. Unequal mass sizes, black separations, an off-center rupture, and four organic petals prevent those readings.
- Features the series must inherit: Exact-black negative space, five-color cost-to-return grammar, one strong motion change, coarse brush treatment, and non-geometric mechanism encoding.
- Features later images must not mechanically copy: The left crescent, four-petal upper-left return, three-source arrangement, or central red fork.

## Hard Gates

- PASS: final image is square and exactly `256x256`.
- PASS: final outer border contains zero non-black pixels; the final contains 47,557 exact `#000000` pixels (72.57%).
- N/A: this target is a card, not a relic.
- PASS: no frame, UI, text, watermark, sun disk, spotlight, recycle symbol, card stack, starburst, or vector geometry appears.
- PASS: the three-source convergence and returned light remain readable at final size.
- PASS: the subject is not constructed from circles, straight lines, regular polygons, rings, or an equally spaced triad.
- PASS: stage 2 uses five visually distinct foreground groups.
- PASS: stage 3 and final retain the same five groups without increasing the count.
- PASS: the final foreground bounding region is approximately 73.8% wide by 77.7% high.

## Scoring

| Dimension | Score | Notes |
| --- | ---: | --- |
| Palette consistency | 21/25 | All five groups remain visible; smoke violet is intentionally compact at the peeling join while gold is slightly stronger to sell the total reversal. |
| Complexity consistency | 23/25 | Three dominant sources, one join, four fragments, and four connected petals remain legible at benchmark density. |
| Silhouette recognition | 27/30 | Unequal left, lower, and right mantles stay distinct from a wing pair or equal triad. |
| Visual effect | 19/20 | The dark convergence and bright directional rebound form a strong, finished focal exchange. |
| **Total** | **90/100** | Passes the series threshold. |

## Generation Record

1. Stage 1 generated three converging dark mantles, peeling Curse shells, one shared rupture, and a warm return.
2. Stage 2 edited away the initial wing-like symmetry, reshaping the sources into unequal left, lower, and right masses and compressing the return into four upper-left petals.
3. Stage 3 edited the composition into broad coarse strokes, removed fine feathers and redundant folds, and retained no more than four shell fragments and four return petals.
4. A background-only chroma edit isolated the unchanged subject. `tools/card_art_finalize.py` composited exact black, fitted the subject to series bounds, and proportionally downsampled to `256x256`.

## Decision

Passes the approved-series hard gates and scoring threshold. Keep as the final preview for `晨星：回光`; installation into shipped mod resources remains separate.
