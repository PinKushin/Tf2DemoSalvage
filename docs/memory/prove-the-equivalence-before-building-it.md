---
name: prove-the-equivalence-before-building-it
description: Two engine code paths that look different can be arithmetically identical for the models that exist — measure the inputs before implementing the second one.
metadata:
  type: project
---

**Before implementing a second engine path that differs from one you already have, ask what the
difference needs in order to be observable — then measure whether that thing exists.**

B349, 2026-09-05. Valve writes a weapon's barrel bone two ways: the world path assigns the whole
quaternion (`AngleQuaternion( RadianEuler( 0, 0, angle ), q[bone] )`), the viewmodel path reads the
existing angles, replaces `a.z`, and writes back. Different code, and it reads like a divergence.

**It is not, for any model TF2 ships**, and three measurements settle it:

- `barrel` and `procedural_chamber` both have **identity bind rotations** — checked against every
  bone in each model, where `c_weapon_stattrack` and two `weapon_bone_N` are the only non-identity
  ones.
- **No animation tracks either bone.** Every animation in both models moves exactly ONE bone,
  `weapon_bone` — at frame 0 and at frame 3, which is the control that makes it a claim about the
  TRACKS rather than about a frame.
- So `q[bone]` is identity when the override runs, and read-modify-write on identity produces the
  same pure-Z quaternion the flat assign produces.

**The difference needed a non-identity rotation on that bone to be visible, and nothing supplies
one.** Implementing the second path would produce identical output.

**Why this matters more than saving the work:** a second implementation of an equivalent path is not
free. It is more code to keep in step, another place for the two to drift, and a future reader has
no way to tell "deliberately duplicated" from "accidentally divergent". Proving the equality and
recording it is the smaller artefact.

**How to apply:**

1. Write the difference as a condition — "these differ only when X".
2. Measure whether X occurs in the shipped data. Usually one probe run.
3. If it does not, record it as a MEASURED non-divergence with what would falsify it, and say so
   plainly. The audit's own rule: when there is no visible symptom, that is a real answer.
4. Make the measurement repeatable. Here it became `model <path>` reporting bind rotations and
   per-animation tracked bones, so the question can be re-asked of any weapon in one call rather
   than re-derived by the next person.

Related: [[the-base-is-not-the-behaviour]], [[unreachable-can-be-proved-not-just-observed]],
[[filing-a-divergence-is-not-fixing-it]], [[an-optimisation-is-not-a-skippable-departure]].
