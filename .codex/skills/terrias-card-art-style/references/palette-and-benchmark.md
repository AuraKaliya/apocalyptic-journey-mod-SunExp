# Palette and Benchmark Evidence

Continue from the benchmark and approval already established for the requested
series. The current conversation is valid approval evidence; absence of a
duplicated approval line in a review file does not revoke it. If the actual
reference image is missing, recover that asset without reopening the decision.

For a new benchmark, locate palette evidence in this order:

1. Current user instructions and approved series design.
2. The relevant [Terrias design document](../../../../docs/Terrias/design)
   and existing series review under tools/previews/card-art.
3. [The scoped palette memo](../../../../卡包色系备忘.txt), only for the
   themes it actually names. It is not a palette for every PackBelong.

Data/CardPack identifies pack membership but does not currently define the art
palette. Do not infer colors or complexity from rarity. When a new series has
no declared palette, make the proposed palette explicit in its required
benchmark review rather than silently treating another series as authority.

The project examples constrain brushwork, silhouette and simplification.
The approved series benchmark constrains that series' hues, value distribution
and complexity. Preserve the scope actually approved by the user.

## Stage-four boundary

The final stage only resizes the accepted square stage-three image. Background
repair, crop, recoloring or subject fitting belong before that boundary.
Historical tools/card_art_finalize.py performs preprocessing as well as
resizing and must not be selected merely because its name says finalize.
Use a verified pure downsampling operation for stage four and inspect its
actual dimensions and preserved composition.
