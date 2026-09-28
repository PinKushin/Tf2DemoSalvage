---
name: two-matrix-conventions-on-purpose
description: Bones stay in Valve's column-vector 3x4 and reach the shader raw; the model matrix is row-vector 4x4. Crossing is a real boundary, and it belongs in one place.
metadata:
  type: project
---

**This renderer speaks two matrix conventions deliberately, and `MatrixConvention` is the boundary.**
Valve's `matrix3x4_t` transforms a COLUMN vector (bones, attachments) and skinning uses it RAW — no
conversion. The model matrix transforms a ROW vector. Using a Valve transform as a model matrix is a
transpose plus a translation move — the cost of two conventions, not a workaround for a wrong one.

**Why written down:** the owner challenged it ("if our conventions are wrong, fix those, don't
transpose around them"); checking found the conventions sound and the real defect was the conversion
existing in TWO PLACES with no statement of which layout was which.

**How to apply:** never transpose inline — call the shared conversion methods. **Test with a
ROTATION** — a missing transpose is invisible on pure translation, so is a reversed multiply order.

---

## `ivp-is-a-third-convention` — physics changes axes, units and storage at once

Source's physics engine (IVP) crosses via one function changing three things: AXES (Source Z-up →
IVP Y-up, `M' = P M Pᵀ` with `P = [[1,0,0],[0,0,−1],[0,1,0]]`), UNITS (×0.0254, metres per inch — the
constant sits at `18011f000` with `0x421d7af6` (`39.3700790`, `1f/0.0254f` in float) in the next
dword, once wrongly carried as the decompiler's decimal `39.37`, a different float `0x421d7ae1`),
STORAGE (`FUN_18000ca70` writes rows into columns of a 4×4, translation to the last row). Two
independent facts confirmed the permutation: a joint axis remap table, and exactly one axis negated
when converting ragdoll limits.

**Why:** getting the axis swap right while missing the transpose, or the units while missing the
sign, produces a ragdoll that's consistently and subtly wrong — settles at a plausible angle in the
wrong place.

**How to apply:** write the conversion in ONE place, test against a transform with no symmetry
(asymmetric translation, rotation about a non-principal axis). Related: [[nothing-is-closed]],
[[a-phy-is-text-except-for-the-hulls]], [[address-a-struct-by-name-not-from-its-end]].

### The inverse was the forward map applied twice, and that is a ROTATION (B400)

**A physics coordinate inverse was spelled identically to the forward map for weeks.** Applying the
forward map twice composes two 90° rotations into 180° — every hull loaded (world, brush entities,
static props, ragdolls) was upside down and backwards. Symptom: corpses falling through floors in
about one column in seven.

**A wrong ROTATION is the hardest wrong transform to see** — symmetric maps mirror nothing, scale
nothing. Nine plausible-sounding measurements came back "correct" (extents, ledge counts, plane
histograms, outward normals) because on a symmetric map, misplaced geometry often lands where other
geometry legitimately is.

**What to reach for instead: an IDENTITY the format guarantees, not a similarity.** World convexes
are built with `NO_SHRINK` (`utils/vbsp/ivp.cpp:1531` — `VPHYSICS_SHRINK 0.5` is brush entities only),
so an axis-aligned world brush and its convex share the same eight corners
exactly — counting exact matches needs no tolerance: 0 of 2,083 as read, 1,964 corrected. **Print
every candidate transform with the identity as the control** — a rival wrong flip still scored 1,771
purely from map symmetry.

**The tests had made the same mistake** — a fixture built by hand to be "independent" reproduced the
identical error because one belief wrote both the fixture and the reader.
**Independence of code is not independence of belief** — a fixture is independent only when its
authority is a different SOURCE. See [[instrument-bugs-outnumber-decoder-bugs]],
[[most-of-a-decoder-is-untested]].

### IVP's interior stays in metres; Hammer units stop at the seam (D173)

Owner, challenging metric conversion in a port: *"valve uses hammer units"* — accepted the answer:
*"Oh ok so parity."* The game side is Hammer units; `vphysics.dll` converts ONCE at its API boundary
(`METERS_PER_INCH (0.0254f)`, `src/public/vphysics_interface.h:40`) and runs IVP entirely in metres
(collision tolerance `(0.25f − 1e-4f) × 0.0254f`, gravity scaled in `SetGravity`, floor constants
`1e-19`, `1e-12`, `1e-10f`, `1e-8` all metre values). A port multiplying back to inches (×39.37) rounds
differently and can't reach bit parity.

**How to apply:** an IVP port takes/returns metres, holds the binary's constants by their bits;
convert only at the same seam the engine does.
