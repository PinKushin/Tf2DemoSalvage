---
name: clip-the-surface-to-the-projection
description: "A decal or overlay is a volume; the fragment is the part of the surface inside it, never the projection cut down by the surface."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:55:27.903Z
---

**Clip the FACE to the overlay, not the overlay to the face.** B134, 2026-08-21. The old builder cut
the overlay's quad with each face's edge planes, bounding every fragment by BSP splits rather than the
band — a uniform stripe arrived as trapezoids with gaps, skewed on faces not parallel to the overlay.

An overlay is a projection volume: clip the face's own polygon against the four planes swept from the
quad's edges along the basis normal. Gives, with no correction step: fragment lies ON the wall
(subset of it), adjacent faces TILE (shared edges, identical clip planes), band is ONE HEIGHT
everywhere (two of the four planes are its own long edges).

**How to apply:**
- A slack fudge is the tell — the old clip needed give to hide seams; the new one needs none. Slack
  to hide a seam usually means the pieces are cut by the wrong thing.
- vbsp's face list is authoritative, never filter it — `Overlay_AddFaceToLists` adds a face because
  the mapper assigned it, no normal test. Filtering here refused 108 of 634 faces on cp_process.
- Check the parse before rewriting geometry — ours was faithful; quads measured 640×64 against
  640×288 faces (U ratio 1.00), which proved the fault was in the fragment builder.
- **Interpolated:** `engine/overlay.cpp` is unpublished; flagged per D44. Owner's steer: *"it's either
  something of Valve's we haven't implemented or somewhere we went different"*.

Related: [[read-the-map-before-the-renderer]], [[a-filed-design-choice-may-not-be-one]],
[[build-time-shortcuts-assume-the-camera]], [[nothing-is-closed]], [[the-denominator-decides-what-can-be-lost]].
