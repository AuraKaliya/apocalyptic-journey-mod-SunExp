---
name: sunexp-card-art-style
description: Project-local skill for drawing or redesigning Witch's Apocalyptic Journey card art and relic icons with a required per-series benchmark approval gate and a real four-step image workflow. Use when artwork must start from the current name, effect, and pack palette; preserve an organic readable silhouette on pure black; be progressively hand-painted and simplified; and finish at 256x256 without rarity-based complexity changes.
---

# Witch Mod Card And Relic Art

Use this skill inside this repository for card art and relic icons. Pair it with
`imagegen` for stages 1-3. Pair it with `sunexp-mod-dev` only when installing
the approved final image into mod resources. Runtime frames, shaders, visual
bundles, and skill CG are outside this skill.

## Example Art

Use these bundled images as the only global style examples:

- `assets/reference-ember-cloak-card.png`
- `assets/reference-solar-scorching-light.png`
- `assets/reference-stellar-overture-turn.png`
- `assets/reference-spirit-ball.png`

They define hand-painted treatment, brush character, silhouette handling, and
the target level of simplification. They do not define the new subject or the
series palette. Do not copy their shapes or reconstruct a target from circles,
straight lines, regular polygons, rings, or other geometric line work.

## Definitions

- A `series` is the target's `PackBelong` card pack.
- A `benchmark image` is the first approved final image for a series.
- Foreground color counts exclude the pure-black background and count visually
  distinct painted colors rather than minor antialiasing pixels.

## Series Approval Gate

Before drawing a complete series:

1. Select one representative card or relic from the series.
2. Complete all four drawing stages for that one target.
3. Reverse-analyze and score the result with
   `references/asset-validation.md`.
4. Present the benchmark image, analysis, and score for review.
5. Stop and wait for explicit approval before drawing the rest of the series.
6. After approval, use that benchmark image as a required reference for every
   remaining target in the series.

Do not generate a whole series before its benchmark is approved. Match every
target to the benchmark's complexity regardless of rarity. Never use rarity to
add or remove colors, shapes, details, effects, or brushwork.

## Four-Step Drawing Workflow

### 1. Complex Theme Image

Read the current card or relic name and effect, then read the series pack
palette. Derive a visual theme and generate an actual high-quality `1:1` image.

- A card theme has no additional subject restriction.
- A relic theme must depict a physical item.
- The background must be pure solid black `#000000`.
- Do not include a UI frame, card frame, or vector geometry.
- Give the subject a readable, organic silhouette and characteristic outer
  contour. Build it from painted masses, not geometric lines.

This stage establishes the full subject, composition, and visual meaning before
any color or detail compression.

### 2. Hand-Painted Simplification

Edit the stage-1 image instead of generating a replacement from text. Reference
the example art and, after series approval, the benchmark image.

- Preserve the subject, composition, and silhouette.
- Stylize the image with visible hand-painted brushwork.
- Reduce the foreground to `4-6` visually distinct colors.
- Merge secondary structures and reduce detail.
- Do not convert the subject into an emblem or geometric line drawing.

### 3. Coarse Final Stylization

Edit the stage-2 image and reference the same example art and approved
benchmark image.

- Reduce the foreground to `3-5` visually distinct colors and never increase
  the stage-2 count.
- Delete tiny details and merge fragmented color patches.
- Convert the remaining forms into large, coarse, rough hand-painted strokes.
- Preserve the readable silhouette and the identity of the subject.
- Do not replace organic contours with circles, straight lines, regular
  polygons, rings, or diagram-like marks.

Stages 1-3 are three separate image operations. Never collapse them into one
prompt that merely describes a staged process.

### 4. Final Resize

Resize the stage-3 image proportionally to exactly `256x256`. Use high-quality
downsampling. Do not regenerate, crop, redraw, recolor, or recompose it during
this step.

## Working Artifacts

Keep reviewable stages outside shipped mod resources:

```text
tools/previews/card-art/<series>/<target>/
  01-theme-source.png
  02-painted-4-6.png
  03-coarse-3-5.png
  04-final-256.png
  review.md
```

Only install `04-final-256.png` after it passes the hard gates and scoring rules
in `references/asset-validation.md`. Use `references/prompt-patterns.md` for the
stage-specific generation and edit prompts.
