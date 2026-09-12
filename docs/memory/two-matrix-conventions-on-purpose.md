---
name: two-matrix-conventions-on-purpose
description: Bones stay in Valve's column-vector 3x4 and reach the shader raw; the model matrix is row-vector 4x4. Crossing is a real boundary, and it belongs in one place.
metadata:
  type: project
---

**This renderer speaks two matrix conventions deliberately, and `MatrixConvention` is the boundary.**

- **Valve's `matrix3x4_t` transforms a COLUMN vector.** Twelve floats, translation in column three.
  Bones and `mstudioattachment_t.local` are both this, and skinning uses them RAW: the shader does
  `dot(boneRows[row], float4(position, 1))`, which is that formula exactly. Nothing is converted for
  skinning and nothing should be.
- **The model matrix transforms a ROW vector.** Sixteen floats, translation in row three, declared
  `row_major float4x4`. `PropTransform.ToMatrix` already produces it.

So using a Valve transform AS a model matrix — an attachment point, say — is a transpose plus a
translation move. That is the cost of having two conventions, not a workaround for a wrong one.

**Why it is written down:** the owner pushed back with "if our matrix conventions are wrong, we
should fix those not transpose around them", which was the right challenge. Checking it found the
conventions sound and the real defect elsewhere: the conversion existed in **two places with two
pieces of code** and no statement anywhere of which layout was which. Two implementations of one
boundary is how they come to disagree, and a disagreement here produces a plausible placement rather
than an error.

**How to apply:** never transpose inline. Call `MatrixConvention.ToModelMatrix`, `.Concatenate`
(Valve's `ConcatTransforms`, kept in Valve's convention because that is the form both operands
arrive in), or `.Multiply`. **Test with a ROTATION** — a missing transpose is invisible on a pure
translation, and so is a reversed multiply order, so a test using only offsets passes against both
bugs ([[a-test-can-outlive-its-design]] on the wider point).

---

## `ivp-is-a-third-convention` — physics changes axes, units and storage at once

Source's physics engine is **IVP** (Ipion Virtual Physics), and every transform crossing into it goes
through `FUN_180002cc0` in `vphysics.dll`, which changes three things at once:

- **Axes.** `Source (x, y, z) → IVP (x, −z, y)`, applied to a rotation as `M' = P M Pᵀ` with
  `P = [[1,0,0],[0,0,−1],[0,1,0]]`. Source is Z-up; IVP is Y-up.
- **Units.** Translations are multiplied by **0.0254** — metres per inch. The constant sits at
  `18011f000` with `39.37` in the next dword.
- **Storage.** `FUN_18000ca70` then writes the rows into COLUMNS of a 4×4 and puts the translation in
  the last row.

Two independent facts confirm the permutation rather than assuming it: the joint axis remap table at
`18011f014` is `00 02 01 03` (Source 1↔2), and exactly one axis is negated when ragdoll limits are
converted — Source axis 2, which is the one `P` gives a minus sign.

**Why:** this project already keeps two matrix conventions on purpose and crosses between them once —
the rule above. IVP is a THIRD, and it is the dangerous kind: getting the axis swap right while
missing the transpose, or the units while missing the sign, produces a ragdoll that is consistently
and subtly wrong rather than obviously broken — a corpse that settles at a plausible angle in the
wrong place.

**How to apply:** anything handed to or taken from physics crosses all three at once. Write the
conversion in ONE place, test it against a transform with no symmetry (asymmetric translation and a
rotation about a non-principal axis), and cite `FUN_180002cc0`. Related:
[[nothing-is-closed]], [[a-phy-is-text-except-for-the-hulls]],
[[address-a-struct-by-name-not-from-its-end]].

### The inverse was the forward map applied twice, and that is a ROTATION (B400, 2026-09-12)

**`ToSource` spelled `(x, −z, y)` in both directions for weeks.** The inverse of `(x, −z, y)` is
`(x, z, −y)`; applying the forward map a second time composes two 90° rotations about X into a 180°
one. Every hull the project loaded — world brushes, brush entities, static props, `.phy` ragdoll
bodies — was upside down and back to front. The symptom was corpses falling through floors in about
one column in seven.

**A wrong rotation is the hardest wrong transform to see, and a symmetric map is the worst place to
look for one.** Nothing is mirrored, nothing is scaled, nothing leaves the coordinate range. Nine
measurements came back correct: map-sized extents, the right ledge count, a brush-shaped plane
histogram, outward normals, spheres containing their hulls, right contents, and individually printed
ledges that were clean axis-aligned boxes. On a 5CP map, symmetric about a diagonal, much of the
misplaced geometry lands where other geometry legitimately is, so even a nearest-neighbour search
returns a plausible answer.

**What to reach for instead: an IDENTITY the format guarantees, not a similarity.** The world's
convexes are built with `NO_SHRINK` (`utils/vbsp/ivp.cpp:1531` — the `VPHYSICS_SHRINK 0.5` is brush
ENTITIES only), so a six-plane axis-aligned world brush and its convex have the same eight corners.
Counting exact box matches needs no tolerance and no plane comparison: 0 of 2,083 as read, 1,964
under the correction. **Print every candidate transform with the identity as the control** — a rival
flip scored 1,771 purely from the map's symmetry, so a probe reporting only its best candidate would
have named the wrong one confidently.

**And the tests had made the same mistake.** A fixture wrote the map out by hand with the comment
*"so this fixture cannot agree with a wrong reader by sharing its arithmetic"* — and reproduced the
identical error, because one belief wrote both. Ten tests round-tripped through it and passed.
**Independence of code is not independence of belief:** a fixture is independent only when its
authority is a different SOURCE. Call the function that was read from the binary. See
[[instrument-bugs-outnumber-decoder-bugs]] and
[[most-of-a-decoder-is-untested]].
