# 41 — What a leaf does not name

*Measured 2026-08-28, on cp_process and cp_badlands.*

A Source BSP leaf carries `firstleafface` and `numleaffaces`, a range into the `LUMP_LEAFFACES`
array of face indices. It is the obvious way to turn "which leaves can I see" into "which surfaces do
I draw", and it is what this project built its world cull on.

**It does not name displacements. Not one of them.**

| map | brush spans | reachable | terrain spans | reachable |
|---|---|---|---|---|
| cp_process_f12 | 12,306 | 12,306 | 60 | **0** |
| cp_badlands | — | — | 1,191 | **0** |

*Evidence class: measured on the corpus, by walking every leaf of the map and collecting the distinct
faces named.*

## Why

`vbsp` builds a leaf's face list in `EmitLeaf` (`utils/vbsp/writebsp.cpp:135`), and the list has
exactly two sources:

```c
// write the leaffaces
leaf_p->firstleafface = numleaffaces;

for (p = node->portals ; p ; p = p->next[s])
    ...
    EmitMarkFace (leaf_p, f);

// emit the detail faces
for ( pList = node->leaffacelist; pList; pList = pList->pNext )
    EmitMarkFace( leaf_p, pList->pFace );
```

A leaf's **portal** faces — the surfaces on its boundary — and its **detail** faces. A displacement is
neither. It is a brush side that `vbsp` converts into a heightfield, referenced from `dface_t` by a
`dispinfo` index and described by `ddispinfo_t` in its own lump.

*Evidence class: read from published source (`source-sdk-2013`).*

`ddispinfo_t` carries no bounding box — `startPosition`, the vert and tri lump offsets, the power, the
neighbours, and the lightmap indices, but nothing spatial that a culler could use directly. So the
engine must compute displacement bounds at load and place them itself. That part is engine-side and
not published; what is published is the absence, and the absence is enough to know that a leaf-keyed
cull cannot be the whole story.

*Evidence class: read from published source, plus an interpolation about what the engine does with
the gap.*

## How it was found

By the owner looking at the screen and saying the ground was missing.

Every automated check was green. The coverage test asked, for each face **named by a visible leaf**,
whether a run covered it — which is vacuously true of a face no leaf ever names. The denominator was
the leaves, so the one category of surface that leaves cannot reach was outside the question being
asked. That is the empty-search trap with a control that looked present and was not: the test even
asserted `checkedFaces > 0`, which passed on twelve thousand brush faces while sixty terrain faces
went uncounted.

The corrected test takes **the spans** as its denominator — everything the full world draws — and
requires that anything dropped is either named by some leaf (where the PVS may legitimately excuse
it) or provably outside the frustum. It has two controls: that leaf-orphaned surfaces exist at all,
and that at least one of them was drawn. The second was added after the first version of the fixed
test still passed with the camera indoors, where all sixty were legitimately off screen.

## What this project does about it

Every face span records the world-space box of the triangles actually written for it — for a
displacement that is the subdivided heightfield, not the flat quad it was built from, because a
hillside rises well outside its base. Spans that no leaf names are culled against that box by the
**frustum only, never by the PVS**: a surface the tree cannot place is a surface whose potential
visibility nothing knows, so the only defensible filter is whether it is on screen.

The count of such spans is logged per map. Sixty is displacements. Twelve thousand would mean the
leaf-face lump was misread — and the picture would look perfect, because everything unreachable is
drawn, while the cull quietly did nothing.

## The number that matters

On badlands, from the viewer's opening overhead camera — which sees most of the map and is therefore
close to the worst case for a cull — the world draws **247,905 of 298,641 corners**, in 136 runs
against 105 uploaded batches. The displacements are 1,191 of the 13,003 spans and were 100% of what
went missing.
