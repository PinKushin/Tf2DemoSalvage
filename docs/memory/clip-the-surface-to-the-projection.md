---
name: clip-the-surface-to-the-projection
description: A decal or overlay is a volume; the fragment is the part of the surface inside it, never the projection cut down by the surface.
metadata:
  type: project
---

**Clip the FACE to the overlay, not the overlay to the face.** B134, 2026-08-21.

The old builder took the overlay's quad, cut it with each face's edge planes, and dropped the
survivor onto the face's plane. Every fragment was therefore bounded by **BSP splits** rather than
by the band, so a uniform stripe arrived as trapezoids of differing heights with gaps between them —
and on a face not parallel to the overlay, each corner moved a different distance onto the plane and
skewed the piece as well.

An overlay is a projection volume. Clipping the face's own polygon against the four planes swept
from the quad's edges along the basis normal gives three properties with no correction step: the
fragment lies **on** the wall because it is a subset of it, adjacent faces **tile** because they
share edges and the clip planes are identical, and the band is **one height everywhere** because two
of the four planes are its own long edges.

**How to apply:**

- **A slack fudge is the tell.** The old clip needed a unit of give to hide seams between fragments;
  the new one needs none because neighbours share the cutting planes exactly. Reaching for slack to
  hide a seam usually means the pieces are being cut by the wrong thing.
- **vbsp's face list is authoritative — never filter it.** `Overlay_AddFaceToLists` adds a face
  because the mapper assigned the overlay to that side; there is no normal test in it. Two filters
  here refused 108 of 634 faces on cp_process, all on the wall stripes, most at 45° — the chamfered
  corners the mapper chose. See [[an-uncoverable-gap-is-usually-your-reader]].
- **Check the parse before rewriting the geometry.** Ours was faithful (BasisU in the unused `z` of
  the first three UV points, V flip in the fourth, face count masked from
  `m_nFaceCountAndRenderOrder`) and the quads measured 640×64 against faces spanning 640×288 — U
  ratio 1.00. That measurement is what proved the fault was in the fragment builder.
- **This is interpolated.** `engine/overlay.cpp` is unpublished and nothing in source-sdk-2013
  touches the lump outside vbsp. Flagged per D44. See [[nothing-is-closed]] for the search order,
  and note the owner's steer that settled it: *"it's either something of Valve's we haven't
  implemented or somewhere we went different"* — which split the search cleanly in two.

Related: [[read-the-map-before-the-renderer]], [[a-filed-design-choice-may-not-be-one]],
[[build-time-shortcuts-assume-the-camera]].
