# 晨星：诅咒 / 众生渡

- Review status: passed internal series validation
- Approved series benchmark: `../morning-star-elegy/04-final-256.png`
- Final asset: `04-final-256.png`
- Built-in image generation mode: `image_gen`
- Final SHA-256: `7BF3CB54484F49CB87D02D3291A2906EC445719B40F914EADC57C409613CA2A1`

## Reverse Analysis

- Subject and visual theme: One broad deep-ultramarine leaf carries three smoke-violet Curse pods from the lower left through a wine-red/crimson fissure; three old-gold/ivory page-petals emerge toward the upper right.
- Pack palette and approximate color-area ratios: Deep ultramarine is the enclosing leaf and largest group; smoke violet marks the input region and pods; wine red and crimson form the conversion seam; old-gold/ivory appears only after the seam. The final canvas contains 71.99% exact black.
- Dominant painted masses: One thick diagonal leaf-vessel, one red conversion fissure, and one purple input shadow.
- Secondary painted masses: Exactly three Curse pods and exactly three output page-petals, kept below the seven-object series limit.
- Characteristic silhouette features: A long hooked lower-left leaf tip, a split red waist, and a pointed upper-right leaf edge create a clear crossing direction without a literal boat.
- Black negative-space structure: Black cuts around every pod and page-petal, opens a notch at the leaf waist, and surrounds the entire diagonal subject with a continuous margin.
- Brush size, density, direction, and edge character: Large lengthwise strokes carry the leaf; short oval strokes close the pods; rough rising strokes open the page-petals; the fissure uses a thick torn diagonal.
- Detail density and focal point: The highest density is at the central conversion seam, with repeated objects simplified to three readable inputs and three outputs.
- What creates the visual impact: Equal visible counts on opposite sides of a single organic crossing make “burn and replace” legible, while the warm output core also suggests restored Mana.
- Geometry or simplification risks: The leaf could become a literal boat and the repeated objects could become a queue or card diagram. The absence of hull anatomy, water, rectangles, arrows, and uniform spacing keeps the image organic.
- Features the series must inherit: Exact-black separation, five-color conversion grammar, one dominant organic silhouette, coarse strokes, and mechanism-bearing objects readable at 256px.
- Features later images must not mechanically copy: The diagonal leaf-vessel, three-to-three count, central fissure position, or lower-left/upper-right routing.

## Hard Gates

- PASS: final image is square and exactly `256x256`.
- PASS: final outer border contains zero non-black pixels; the final contains 47,177 exact `#000000` pixels (71.99%).
- N/A: this target is a card, not a relic.
- PASS: no frame, UI, text, writing marks, watermark, boat anatomy, water, card rectangles, portal, or vector geometry appears.
- PASS: the organic leaf crossing and before/after objects remain readable at final size.
- PASS: the subject is not constructed from circles, straight lines, regular polygons, rings, or diagram marks.
- PASS: stage 2 uses the five declared foreground groups.
- PASS: stage 3 and final retain the same five groups and do not increase their count.
- PASS: the final foreground bounding region is approximately 73.8% wide by 76.2% high.
- PASS: exactly three input pods and exactly three output page-petals remain distinguishable.

## Scoring

| Dimension | Score | Notes |
| --- | ---: | --- |
| Palette consistency | 23/25 | Five groups follow the benchmark value hierarchy, with warm color restricted to the conversion and output side. |
| Complexity consistency | 23/25 | One major silhouette, three dominant masses, and six repeated objects match the benchmark’s upper readable density. |
| Silhouette recognition | 27/30 | The thick diagonal leaf and split waist remain distinct at 256px without becoming a literal vessel. |
| Visual effect | 19/20 | Equal input/output counts and the red conversion seam create a particularly clear transformation action. |
| **Total** | **92/100** | Passes the series threshold. |

## Generation Record

1. Stage 1 generated a broad curved leaf-vessel, incoming Curse pods, a torn red core, and rising page-like output petals.
2. Stage 2 edited the composition against the approved benchmark, removed writing-like marks, reduced the repetition to three inputs and three outputs, and compressed the palette to five groups.
3. Stage 3 edited the leaf and repeated objects into coarse masses; a focused count correction replaced the compound upper bloom with one large page-petal so the output became exactly large, medium, and small petals.
4. A background-only chroma edit isolated the unchanged subject. `tools/card_art_finalize.py` composited exact black, fitted the subject to the benchmark bounds, and proportionally downsampled to `256x256`.

## Decision

Passes the approved-series hard gates and scoring threshold. Keep as the final preview for `众生渡`; installation into shipped mod resources remains separate.
