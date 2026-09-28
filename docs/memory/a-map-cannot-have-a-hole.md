---
name: a-map-cannot-have-a-hole
description: A gap is never in the map; name which of the parallel readings of it has the gap.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T03:56:18.056Z
---

Owner, 2026-09-07: *"they literally cant have holes because hammer doesnt allow holes, so idk what
you mean by holes"*.

**Why:** a `.bsp` is compiled from sealed solids. This project holds four parallel readings of one
map (physics ledges from `LUMP_PHYSCOLLIDE`, terrain from `LUMP_DISPINFO`, brush tree from
`LUMP_BRUSHES`, a probe's assembly) — "the map has a hole" wrongly collapses all four into a claim
about Valve's data.

**How to apply:** name the reading and give it a denominator before reporting a gap. Here:
`LUMP_BRUSHES` declares 2,722 solid brushes, `LUMP_PHYSCOLLIDE` yields 2,671 ledges, vbsp writes one
convex per referenced brush — the physics reading was complete; the gap was in the CAMERA's reading,
which stops on displacement base brushes vphysics deliberately has no collision for.

**A COUNT can be complete while the geometry is wrong-placed** (B400, 2026-09-12) — the 2,722-vs-2,671
measurement stayed correct while every ledge was rotated 180° about X (rotation preserves counts,
extents, plane totals, contents histograms). "Complete" ≠ "right", and that kept a physics-side defect
looking camera-side for days. **Pair every completeness count with one format-guaranteed IDENTITY**
(e.g. an axis-aligned world brush and its convex share eight corners exactly, `NO_SHRINK`).

---

## `a-hole-is-not-always-a-drawing-fault` — ask what CLASS of geometry could occupy it

Black map-view areas were chased through shading and culling explanations before the cause was
**static props** (`prop_static` in BSP game lump 35, `sprp`), unread entirely.
`tools/toolsinvisibledisplacement` is collision-only terrain; skipping it is correct, but a prop
(rock, crate) sits on top of it and was never drawn — skip the tool material, draw the props, the
hole is prop-shaped.

**Why:** a coverage grid built from faces can report "no drawn face here" but not "the missing thing
was never a face."

**How to apply:** ask what class of geometry could occupy a hole before asking which filter dropped
it — region-shaped means geometry, material-shaped means shading, "no geometry of a kind you parse"
is a third answer. The owner named it from memory of playing the map.

---

## `the-engine-may-load-what-you-rebuild` — read the engine's loader before approximating a piece

**Terrain thickness was a 512-unit slab "standing in for `buildOuterHull`"; the map carried the hull
all along** (B369, 2026-09-12). vbsp writes one outer hull per displacement into `LUMP_PHYSDISP`
(lump 28); the engine loads it at init and hands it to vphysics as `pHull`, building one only when
absent. This project rebuilt terrain from render lumps, never opened lump 28.

**Finding the unread piece is not finding the cause:** the first write-up said the hull was the
missing thickness under buried limbs; reading vphysics' surface manager refuted it same-day — the
hull is the ROOT of a two-level query, not a solid; the engine has no terrain thickness at all. The
limbs were buried by our narrow phase. Read the CONSUMER before saying what an unread piece would
have fixed.

**Why it survived:** `CDispCollTree::GetVirtualMeshList` sets `pHull = NULL` in the SDK base; the
engine's runtime handler calls it and then overwrites the field — [[the-base-is-not-the-behaviour]]
in a file format.

**How to apply:** before approximating anything the engine clearly has, list the lumps its LOADER
reads (`CollisionBSPData_Load*` log strings, in order) and find which is unread. Measure with two
controls: count against an agreeing lump, and declared sizes against actual length.
