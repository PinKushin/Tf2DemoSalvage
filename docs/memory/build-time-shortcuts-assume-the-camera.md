---
name: build-time-shortcuts-assume-the-camera
description: Culls and biases tuned for the top-down view broke the moment a free camera existed; a render decision that depends on the viewpoint belongs per frame, not in the geometry build.
metadata:
  type: project
---

**A shortcut justified by "you cannot see it from here" encodes the camera into the geometry, and
stops being true the moment the camera moves.** Three defects in one evening, all invisible top-down,
all obvious minutes after a free camera existed:

- `MapWorld` discarded every downward-facing face at BUILD time, called "the engine's own backface
  culling" — deleted ceilings, undersides. Valve culls per frame against frustum + PVS.
- Decal depth bias retuned from Valve's `-262144` to `-10000` because bias is a fraction of the depth
  RANGE and orthographic projection spreads that over a map's height — correct for that projection
  only (B70 tracks perspective).
- `DrawTranslucent` left a read-only depth state set, so models drew with no depth writes (eyes
  through the back of a head; submission order beat distance between models).

**How to apply:** put viewpoint-dependent decisions in the per-frame path; let a pass establish its
own state. When a shortcut is worth taking, tie it to what it assumes (read the projection off the
matrix, not a caller's flag defaulting wrong).

**Meta-lesson:** early workarounds get replaced wholesale, and their TESTS must go with them —
inverted, not deleted, so the requirement becomes the thing under guard.

Related: [[a-test-can-outlive-its-design]], [[instrument-bugs-outnumber-decoder-bugs]].

---

## Three more instances in one day (2026-08-21), pattern now specific

| shortcut | true under | written as |
|---|---|---|
| decal depth bias `2^24/worldRange` (B135) | ortho camera, depth linear in height | "about one world unit" |
| height cut `clip(SV_POSITION.z - cut)` (B136) | same camera, looking straight down | "the cut is on depth, which is height" |
| reflections needing an eye position (B126) | any perspective camera | — |

**Pattern: a quantity DERIVED under one projection and FUNDAMENTAL under none gets written as
whichever is cheaper, and its comment records the coincidence as a definition.** Tell: a comment "X is
Y" where X and Y are different quantities that happen to coincide.

**Owner's direction (D49):** the overhead view is a *placement*, not a projection — keeping a second
projection to express a camera position is what generated all three.
