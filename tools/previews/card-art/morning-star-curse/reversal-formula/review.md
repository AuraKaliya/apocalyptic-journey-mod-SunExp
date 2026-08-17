# 晨星：诅咒 / 逆转术式

- Review status: passed internal series validation
- Approved series benchmark: `../morning-star-elegy/04-final-256.png`
- Final asset: `04-final-256.png`
- Built-in image generation mode: `image_gen`
- Final SHA-256: `D5FD43C373E2EFA5F3C138062AFA3E3F942EDBF1A6DFAA7B2A707B1CFB99ACB3`

## Reverse Analysis

- Subject and visual theme: A single thorned Curse seed pod is peeled open by two asymmetric deep-ultramarine leaf-petals. Its wine-red core is drawn toward the upper right by one crimson mass while the newly exposed inner faces turn old-gold/ivory.
- Pack palette and approximate color-area ratios: The five required groups remain visible. Deep ultramarine is the largest structural group; smoke violet forms the husk; wine red and crimson share the extracted core; old-gold/ivory marks only the reversed inner faces and shed fragments. The final canvas contains 71.43% exact black.
- Dominant painted masses: One smoke-violet lower husk, two deep-ultramarine outer leaf-petals, one wine-red/crimson extracted core, and two connected old-gold inner faces.
- Secondary painted masses: Four uneven shed husk fragments continue the reversal direction without becoming counters or a list of cards.
- Characteristic silhouette features: A low thorned pod base, two mismatched hooked leaves, a black central split, and one tapered diagonal core make the subject identifiable from its contour.
- Black negative-space structure: Black separates the two leaf-petals, cuts through the pod split, isolates every shed fragment, and leaves a continuous outer margin. The final border contains zero non-black pixels.
- Brush size, density, direction, and edge character: Broad hooked strokes describe the pod and leaf-petals; a single rough tapered stroke carries the core toward the upper right; dry-brush serration keeps the gold inner faces organic.
- Detail density and focal point: Detail concentrates at the black split where violet shell, red core, and gold reversal meet. The outer margins and detached fragments remain sparse.
- What creates the visual impact: The same organic body visibly turns from enclosed violet damage to exposed gold benefit, so the first read is “turn inside out” rather than generic burning.
- Geometry or simplification risks: The paired leaves could drift toward a symmetrical emblem, and the red core could read as ordinary flame. Unequal leaf lengths, the off-axis split, the heavy pod base, and coarse non-repeating fragments prevent those readings.
- Features the series must inherit: Exact-black negative space, five-color palette, one characteristic organic silhouette, coarse directional strokes, restrained secondary objects, and visible conversion from curse damage to warm benefit.
- Features later images must not mechanically copy: The paired-hook leaf arrangement, central red spear shape, four detached fragments, lower-left pod base, or this exact diagonal.

## Hard Gates

- PASS: final image is square and exactly `256x256`.
- PASS: final outer border contains zero non-black pixels; the final contains 46,814 exact `#000000` pixels (71.43%).
- N/A: this target is a card, not a relic.
- PASS: no UI frame, card frame, text, watermark, or vector construction appears.
- PASS: the split seed pod has a readable characteristic organic silhouette.
- PASS: the subject is not constructed from circles, straight lines, regular polygons, rings, or star-chart geometry.
- PASS: stage 2 uses the five declared foreground color groups.
- PASS: stage 3 and final use the same five groups and do not increase the stage-2 count.
- PASS: the final foreground bounding region is approximately 75.0% wide by 77.7% high and remains surrounded by black.

## Scoring

| Dimension | Score | Notes |
| --- | ---: | --- |
| Palette consistency | 22/25 | All five groups are present and deep ultramarine remains structural; the directional red core is deliberately stronger than in the benchmark but does not introduce another hue. |
| Complexity consistency | 23/25 | Four dominant masses and four secondary fragments match the benchmark density without rarity-driven elaboration. |
| Silhouette recognition | 27/30 | The split thorned pod and mismatched hooked leaves remain readable at 256px. |
| Visual effect | 18/20 | The black split and opposing violet-to-gold surfaces give the reversal a clear focal action. |
| **Total** | **90/100** | Passes the series threshold. |

## Generation Record

1. Stage 1 generated one large thorned Curse pod at lower left, split by two asymmetric deep-ultramarine leaf-petals; a wine-red core and crimson pull move upper right while the reversed inner faces turn old-gold/ivory.
2. Stage 2 edited that composition against the approved `晨星：悲歌` benchmark and global examples, compressed it to five foreground groups, and merged fine pod anatomy into broad hand-painted masses.
3. Stage 3 edited the stage-2 image into coarse strokes, reduced small fragments, shortened the red extraction plume, and corrected subject scale to the benchmark’s negative-space density.
4. A background-only chroma-key edit isolated the unchanged stage-3 subject. `tools/card_art_finalize.py` composited it onto exact black and performed the proportional high-quality `256x256` downsample.

## Decision

Passes the approved-series hard gates and scoring threshold. Keep as the final preview for `逆转术式`; installation into shipped mod resources remains a separate step.
