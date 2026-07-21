# Stage Prompt Patterns

Run one image operation per target per stage. Stage 2 must edit the stage-1
image, and stage 3 must edit the stage-2 image. Do not replace either edit with
a fresh text-only generation.

When creating a series benchmark, use the bundled example art as style
references. After benchmark approval, use both the bundled example art and the
approved benchmark image for stages 2 and 3.

## Stage 1: Card Theme Image

```text
Generate a high-quality 1:1 card artwork image.
Card name: <name>.
Card effect: <effect>.
Pack palette: <palette>.
Visual theme: <theme derived from the name, effect, and palette>.

Develop the visual theme into a complete, complex image before simplification.
The subject matter is otherwise unrestricted. Use a pure solid black #000000
background. Do not include a UI frame, card frame, or vector geometry. Give the
subject a readable, organic silhouette and a characteristic outer contour made
from painted masses. Do not construct the subject as a logo, emblem, diagram,
geometric line drawing, circle arrangement, regular polygon, or ring pattern.
```

## Stage 1: Relic Theme Image

```text
Generate a high-quality 1:1 relic artwork image.
Relic name: <name>.
Relic effect: <effect>.
Pack palette: <palette>.
Visual theme: <physical item derived from the name, effect, and palette>.

Depict a clear physical item and develop it into a complete, complex image
before simplification. Use a pure solid black #000000 background. Do not
include a UI frame, card frame, or vector geometry. Give the item a readable,
organic silhouette and a characteristic outer contour made from painted
masses. Do not construct the item as a logo, emblem, diagram, geometric line
drawing, circle arrangement, regular polygon, or ring pattern.
```

## Stage 2: Hand-Painted Simplification

Attach the stage-1 image, the bundled example art, and the approved benchmark
image when one exists.

```text
Edit the stage-1 image rather than inventing a replacement composition.
Preserve its subject, meaning, composition, and readable organic silhouette.
Use the attached example art for hand-painted brush character and the approved
benchmark for series palette and complexity.

Stylize, brush-paint, and simplify the image. Reduce the foreground to 4-6
visually distinct colors from the pack palette, excluding the pure-black
background. Merge secondary structures and reduce detail while preserving the
main forms. Use visible painterly strokes instead of smooth vector edges. Do
not turn the subject into a logo, emblem, diagram, geometric line drawing,
circle arrangement, regular polygon, or ring pattern. Keep the background pure
solid black #000000 and keep the canvas 1:1.
```

## Stage 3: Coarse Final Stylization

Attach the stage-2 image, the bundled example art, and the approved benchmark
image when one exists.

```text
Edit the stage-2 image rather than generating a new composition. Preserve its
subject identity, main composition, and readable organic silhouette. Use the
attached example art for final brush treatment and the approved benchmark for
series palette and complexity.

Reduce the foreground to 3-5 visually distinct colors from the pack palette,
excluding the pure-black background, and do not increase the previous color
count. Delete tiny details, merge fragmented color patches, and express the
remaining forms with large, coarse, rough hand-painted strokes. Preserve the
characteristic outer contour. Do not replace organic forms with circles,
straight lines, regular polygons, rings, vector geometry, or diagram-like
marks. Keep the background pure solid black #000000 and keep the canvas 1:1.
```

## Stage 4: Resize

Stage 4 is not an image-generation prompt. Resize the accepted stage-3 image
proportionally to `256x256` with high-quality downsampling and no crop or visual
changes.
