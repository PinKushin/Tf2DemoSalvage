---
name: read-the-map-before-the-renderer
description: Four renderer theories died before asking the BSP what the broken thing actually was; the face list an overlay names is data, and it had the answer.
metadata:
  type: project
---

**When something in a map draws wrongly, ask the map what it IS before theorising about the
renderer.** The wall stripes in cp_process drew off the walls, and four explanations were proposed
and killed by measurement — a reader offset, the decal depth bias, faces removed by the normal cull,
and entity brushwork placed without its origin. All four were about the rendering chain.

The answer was in the BSP: they are overlays (`overlays/stripe_red` 45 times,
`concrete/stripe_blue` 43), and what separates them from a sign that always looked correct is how
much they span — a stripe names **1 to 18 faces, median 3**, where `signs/redstone` names 2.

An overlay's face list is the set of surfaces to CLIP against, not candidates to pick one from. The
builder took the first face sharing an orientation and drew one flat quad, which for anything
crossing a corner is a plane cutting through the building. Valve clips the polygon per face; so does
this now, with the fragment dropped onto each face's plane.

**How to apply:** the map states what every material is, which faces use it, and which overlays use
it. One probe over the lumps answered in minutes what four rounds of renderer reasoning did not.
Prefer identifying the object to theorising about the pipeline that drew it.

**A trap inside the fix**, worth keeping: an edge's inward normal is the face normal crossed with the
edge, and which way that points depends on the outline's winding — a BSP carries both. Assuming one
clips every fragment to nothing, which is indistinguishable from an overlay missing its face and
invisible in the counts. Settle it per edge against the face centroid.

Related: [[closed-source-check-the-public-api]] and [[arithmetic-settles-disputes]].
