---
name: a-delta-animation-is-not-a-pose
description: A STUDIO_DELTA layer adds; its absent bones are identity, not the rest pose; and the flag lives on the animation as well as the sequence.
metadata:
  type: reference
---

**Every TF2 player gesture is a DELTA, and composing one as a pose lays the player flat.** Measured
on `scout.mdl`: `PRIMARY_reload_start` and `jumpland_primary` both carry `STUDIO_DELTA` on the
sequence AND on the animation behind it, and both carry `STUDIO_POST`.

**`SlerpBones` splits on it before anything else** (`bone_setup.cpp:1434`):

```
if ( seqdesc.flags & STUDIO_DELTA )
{
    if ( seqdesc.flags & STUDIO_POST ) QuaternionMA( q1[i], s2, q2[i], q1[i] );  // q1 * (s2*q2)
    else                               QuaternionSM( s2, q2[i], q1[i], q1[i] );  // (s2*q2) * q1
    pos1[i] = pos1[i] + pos2[i] * s2;
    return;
}
```

**Four places have to agree, and getting any one wrong looks like a different bug:**

1. **The composition** — add, do not blend toward.
2. **The seed.** `CalcVirtualAnimation` (`bone_setup.cpp:933`) branches on the ANIMATION's flags:
   a delta's untouched bone is identity and zero, an ordinary animation's is the sequence model's
   bind pose. Seeding a delta from the rest pose makes every unanimated channel a whole bone
   transform, and adding that to a base stretches every limb by its own rest offset.
3. **Any densifying step in between.** A blend that fills absent bones to interpolate two frames
   must fill them the same way. `jumpland_primary` animates twelve bones of seventy-eight; expanded
   against the rest pose it became a seventy-six-bone difference and threw the arms over the head.
4. **`QuaternionScale` is not a component multiply.** It scales the ANGLE —
   `sinsom = sin( asin( sinom ) * t )` — and carries the sign of `w` across, which Valve comments
   as *"keep sign of rotation"* (`mathlib_base.cpp:1757`).

**The flag lives in two places and they are different fields.** `seqdesc.flags` is what `SlerpBones`
tests; `animdesc.flags` is what `CalcVirtualAnimation` tests. Reading one and calling it the other
cost an hour here — the SEQUENCES named in `<class>_animations.mdl` carry `STUDIO_HIDDEN` (`0x400`)
and reading those made "not a delta" look established.

**And the index spaces are different too.** A merged sequence number is not the root model's own
sequence number. Comparing merged 243 against the `model` probe's root list gave the right label
for the wrong reason and sent the whole investigation sideways; ask the merged table for its own
label.

Related: [[one-look-can-be-two-mechanisms]], [[a-property-name-needs-its-declaring-table]].
