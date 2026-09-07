# Event CG artwork

The production catalog is `event-cg.art.json` (schema 1). AuraToolsExp owns these
logical assets and resolves their package-relative files locally. Runtime RPCs
never include these paths or the portrait framing metadata.

Current coverage: 15 character appearances, 6 event poses, 7 painted themes,
32 image resources. Ordinary and alternate appearances are distinct entries.
Missing event poses deliberately select the same appearance's neutral portrait;
an unregistered custom skin first uses its own Character/CareerImage resource.

The six poses are Caroline victory/defeat, Coco victory/ritual, Amelia victory,
and WuNa victory. The remaining planned poses have not been generated in this
iteration. Companion images share the exact source canvas of their parent pose.

Artwork sources and processing prompts are archived under
`output/imagegen/event-cg` in the development workspace. The scene-theme source
prompts are in `runtime-themes`; the generation tool did not disclose a verifiable
underlying image-model identifier. Packaged PNGs are prepared with
`tools/Build-EventCgArtPackage.py`, which needs the original reference directory
only during authoring. The shipped MOD does not depend on that external directory.

Current rendering, lifecycle, protocol, and validation requirements are documented
in `docs/AuraToolsExp/team-event-cg-poster-v3-design.md` at the repository root.
