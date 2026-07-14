# Benchmark Review And Scoring

Use this review for the first benchmark image and every later image in its
series. Hard gates are pass/fail. Score only images that pass every hard gate.

## Hard Gates

- The image is square, and the final artifact is exactly `256x256`.
- The background is pure solid black `#000000`, including the outer edges.
- A relic depicts a physical item.
- No UI frame, card frame, or vector geometry appears.
- The subject has a readable, characteristic silhouette and outer contour.
- The subject is not primarily constructed from circles, straight lines,
  regular polygons, rings, or other geometric line work.
- Stage 2 uses `4-6` visually distinct foreground colors.
- Stage 3 and the final image use `3-5` visually distinct foreground colors and
  do not increase the stage-2 count.

Antialiasing pixels do not count as additional colors. Evaluate the intended
painted color groups.

## Reverse Analysis

Create `review.md` beside the staged images and record:

```markdown
# <series> / <target>

- Subject and visual theme:
- Pack palette and approximate color-area ratios:
- Dominant painted masses:
- Secondary painted masses:
- Characteristic silhouette features:
- Black negative-space structure:
- Brush size, density, direction, and edge character:
- Detail density and focal point:
- What creates the visual impact:
- Geometry or simplification risks:
- Features the series must inherit:
- Features later images must not mechanically copy:
```

This analysis defines why the image works. Later targets inherit its palette,
complexity, silhouette quality, and brush treatment without copying its subject
or exact shapes.

## Scoring

Score out of 100:

| Dimension | Points | Evaluation |
| --- | ---: | --- |
| Palette consistency | 25 | Match the declared pack palette for the benchmark; for later images, match the benchmark's hues, values, foreground color count, and approximate color-area distribution. |
| Complexity consistency | 25 | Match the example-art complexity for the benchmark; for later images, match the benchmark's number and scale of dominant masses, detail density, negative space, and brush density. |
| Silhouette recognition | 30 | The subject remains identifiable from its outer contour and major masses; the contour is characteristic, organic, and not dependent on geometric lines. |
| Visual effect | 20 | The image has a clear focal point, balanced composition, convincing hand-painted strokes, visual force, and a finished high-quality appearance. |

An image passes when:

- every hard gate passes;
- the total score is at least `85/100`;
- silhouette recognition is at least `24/30`;
- palette consistency is at least `20/25`; and
- complexity consistency is at least `20/25`.

## Series Complexity Rule

The approved benchmark fixes the complexity target for the entire series.
Rarity has no effect on complexity. Do not give rare cards more colors, more
details, denser effects, or more elaborate composition, and do not simplify
common cards because of rarity.

## Approval Record

Record the four scores, total, hard-gate result, and review decision in
`review.md`. For the first target in a series, stop after review and wait for
explicit user approval. Only an approved benchmark may be used to draw the
rest of that series.
