---
name: build-time-shortcuts-assume-the-camera
description: Culls and biases tuned for the top-down view broke the moment a free camera existed; a render decision that depends on the viewpoint belongs per frame, not in the geometry build.
metadata:
  type: project
---

**A rendering shortcut justified by "you cannot see it from here" encodes the camera into the
geometry, and stops being true the moment the camera moves.** Three separate defects in one evening,
all invisible from the top-down view and all obvious within minutes of the free camera existing:

- `MapWorld` discarded every face whose normal pointed downward, at BUILD time, and the comment
  called it "the engine's own backface culling". It deleted ceilings, undersides and any wall
  tipping slightly below horizontal. Valve culls per frame against the frustum and the PVS.
- The decal depth bias was retuned from Valve's `-262144` to `-10000` because a depth bias is a
  fraction of the depth RANGE and the orthographic projection spreads that over a whole map's
  height. Correct for that projection only; B70 tracks returning it under perspective.
- `DrawTranslucent` left a read-only depth state set, and models drew after it. With no depth writes
  a model's own triangles stop occluding each other — eyes through the back of a head — and between
  models submission order beats distance, so a medkit drew over a medic from every angle.

**How to apply:** put viewpoint-dependent decisions in the per-frame path, and let a pass establish
the state it needs rather than trusting the previous pass to have restored it. When a shortcut is
genuinely worth taking, tie it to the thing it assumes — the decal bias should read the projection
off the matrix, not take a caller's flag that defaults to the wrong answer.

**And the meta-lesson the owner named:** early workarounds get replaced wholesale, and their TESTS
go with them. `Build_DownwardFacingSurfaces_AreDropped` was pinning the workaround in place and had
to be inverted, not deleted, so the requirement became the thing under guard.

Related: [[a-test-can-outlive-its-design]], [[instrument-bugs-outnumber-decoder-bugs]].
