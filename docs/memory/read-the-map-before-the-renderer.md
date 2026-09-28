---
name: read-the-map-before-the-renderer
description: Four renderer theories died before asking the BSP what the broken thing actually was; the face list an overlay names is data, and it had the answer.
metadata:
  type: project
---

**When something in a map draws wrongly, ask the map what it IS before theorising about the
renderer.** Wall stripes drawing off the walls survived four renderer explanations (reader offset,
depth bias, normal cull, brush placement) before the answer turned out to be in the BSP: they're
overlays, spanning up to 18 faces where a normal sign spans 2.

An overlay's face list is the set of surfaces to CLIP against, not candidates to pick one from — the
builder took the first matching-orientation face and drew one flat quad, cutting through the building
at any corner. Fixed by clipping the polygon per face, dropping the fragment onto each face's plane.

**How to apply:** the map states what every material is, which faces use it, which overlays use it.
One probe over the lumps answered in minutes what renderer reasoning didn't. Prefer identifying the
object to theorising about the pipeline that drew it.

**A trap inside the fix:** an edge's inward normal depends on the outline's WINDING, which a BSP
carries either way — assuming one clips a fragment to nothing, indistinguishable from a missing face.
Settle it per edge against the face centroid.

Related: [[nothing-is-closed]], [[arithmetic-settles-disputes]].
