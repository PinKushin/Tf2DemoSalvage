---
name: ivp-is-a-third-convention
description: Crossing into Source's physics changes handedness, units AND matrix order at once — Source (x,y,z) becomes IVP (x,-z,y), in metres, transposed.
metadata:
  type: reference
---

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

**Why:** this project already keeps two matrix conventions on purpose and crosses between them once
([[two-matrix-conventions-on-purpose]]). IVP is a THIRD, and it is the dangerous kind: getting the
axis swap right while missing the transpose, or the units while missing the sign, produces a ragdoll
that is consistently and subtly wrong rather than obviously broken — a corpse that settles at a
plausible angle in the wrong place.

**How to apply:** anything handed to or taken from physics crosses all three at once. Write the
conversion in ONE place, test it against a transform with no symmetry (asymmetric translation and a
rotation about a non-principal axis), and cite `FUN_180002cc0`. Related:
[[absent-from-the-sdk-is-not-unreadable]], [[a-phy-is-text-except-for-the-hulls]],
[[address-a-struct-by-name-not-from-its-end]].
