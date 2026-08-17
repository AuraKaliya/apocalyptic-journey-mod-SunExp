# 虚假的黄金梦 / 鎏金蝴蝶

- Review status: approved by user on 2026-08-17
- Final asset: `04-final-256.png`
- Built-in image generation mode: `image_gen`
- Final SHA-256: `DBBDB2E4E88729D88C56921A2828F09DEDCE920257D211A01D9A8FC02B54E03D`

## Reverse Analysis

- Subject and visual theme: A living golden butterfly in a forceful upward wingbeat. Its gilded wings are already beginning to tear into painted flakes, expressing the promise and instability of Golden Dream attachment.
- Pack palette and approximate color-area ratios: The final canvas is about 61% pure-black negative space. Within the foreground, pale ivory-gold is about 28%, honey gold about 43%, old-gold/umber about 25%, and crimson cracks about 4%.
- Dominant painted masses: Four broad wing masses form the primary silhouette; the narrow insect body and two antennae establish that the subject is alive rather than an ornament.
- Secondary painted masses: A restrained set of large gold flecks extends the rightward wingbeat and balances the long lower-left wing tip.
- Characteristic silhouette features: Tall split upper wings, ragged lower wings, a long tapering abdomen, two curved antennae, and asymmetric torn tips remain readable at 256px.
- Black negative-space structure: Black separates all four wings around the body, leaves open space above the antennae, and frames the torn edges without a halo or vignette.
- Brush size, density, direction, and edge character: Large directional strokes follow the wingbeat from the body outward. Interior strokes are broad and layered; exterior edges are coarse, torn, and organic.
- Detail density and focal point: Detail is concentrated at the thorax and the inner wing roots. Outer wings use larger masses and fewer marks so the focal point remains the living body.
- What creates the visual impact: Strong gold-on-black value contrast, the upward four-wing spread, and small crimson fractures make the butterfly feel valuable but structurally false.
- Geometry or simplification risks: Bilateral wings could drift toward a crest or brooch. The living body, curved antennae, uneven tearing, and motion flecks must remain in later use.
- Features the series must inherit: Pure-black negative space, four foreground color groups, coarse directional brushwork, readable organic silhouettes, restrained crimson fracture accents, and similar foreground coverage.
- Features later images must not mechanically copy: Butterfly anatomy, the four-wing layout, the exact diagonal tears, the same fleck positions, or the central body-shaped focal point.

## Hard Gates

- PASS: final image is square and exactly `256x256`.
- PASS: final outer border contains zero non-black pixels; the final contains 39,966 exact `#000000` pixels.
- N/A: this target is a card, not a relic.
- PASS: no UI frame, card frame, typography, watermark, or vector construction.
- PASS: the subject has a readable, characteristic organic silhouette.
- PASS: the subject is not constructed primarily from circles, straight lines, regular polygons, or rings.
- PASS: stage 2 uses five visually distinct foreground groups.
- PASS: stage 3 and the final use four visually distinct foreground groups and do not increase the stage-2 count.

## Scoring

| Dimension | Score | Notes |
| --- | ---: | --- |
| Palette consistency | 23/25 | Gold, umber, ivory, and crimson clearly establish the false-gold palette without extra hues. |
| Complexity consistency | 23/25 | Four dominant masses and a controlled fleck count match the current simplified card-art density. |
| Silhouette recognition | 29/30 | The butterfly remains immediately recognizable at 256px and does not read as jewelry. |
| Visual effect | 19/20 | Strong focal contrast and wing motion produce a finished, forceful card face. |
| **Total** | **94/100** | Passes the benchmark threshold. |

## Generation Record

1. Stage 1 prompt: develop one living gilded butterfly on pure black, with a vigorous asymmetric wingbeat, visible anatomy, torn gold motion flecks, and crimson fracture-light; prohibit jewelry, emblems, geometry, frames, and text.
2. Stage 2 edit prompt: preserve the stage-1 subject and composition, convert it to broad hand-painted masses, reduce it to five foreground color groups, and remove photographic metallic detail.
3. Stage 3 edit prompt: preserve identity and silhouette, reduce it to four coarse foreground color groups, retain only substantial motion flecks, and remove remaining tiny detail.
4. Technical background pass: use a flat chroma-key edit plus the bundled background-removal helper to composite the unchanged subject onto exact black, then resize proportionally with high-quality bicubic downsampling.

## Decision

Approved as the visual benchmark for the `false-gold-dream` series. Install the final image as the Gilded Butterfly card face and use it as a required reference for the card-pack cover and later series artwork.
