---
name: a-phy-is-text-except-for-the-hulls
description: Masses, bone names, joint limits and friction are plain KeyValues in a .phy; only the Havok hulls are closed — and every shipped file ends with a trailing block that hides a reader bug.
metadata:
  type: project
---

**"We would need a physics engine to read a `.phy`" is wrong, and this entry exists because that
conclusion was reached once already.** The file is:

1. `phyheader_t` — `int size; int id; int solidCount; int32 checkSum;`, sixteen bytes
   (`phyfile.h:14-21`).
2. `solidCount` collision hulls in Havok's `IVPS` format, which **is** closed.
3. **Plain-text KeyValues** carrying everything else.

`PhysicsModel` in `Tf2DemoSalvage.Content` reads 1 and 3: per solid the index, **bone name**,
parent, surfaceprop, mass, inertia, damping, rotdamping, volume and drag; per joint the parent,
child and three axes of minimum, maximum and friction. Only the shapes are missing.

**`name` is the load-bearing field.** Constraints refer to solids by INDEX, so without a bone name
per solid the joint graph is a set of numbers about an unknown ordering.

The engine dispatches on the same two block names — `solid` and `ragdollconstraint`
(`ragdoll_shared.cpp:283-293`) — and the field names come from `solid_t`
(`vcollide_parse.h:16-24`), `objectparams_t` (`vphysics_interface.h:1062-1075`) and
`constraint_axislimit_t` (`constraints.h:61-79`), where the file's `friction` is Valve's `torque`.

Every class carries a tree, one fewer constraint than solids: demo and pyro 15/14, heavy 16/15,
scout and sniper 17/16, engineer 18/17, medic 24/23. The heavy's solids total **102.0 kg**.

## Two traps, both found by sabotage

**A trailing block hides a missing block-close.** Every `.phy` TF2 ships ends with an `editparams`
block, so a reader that only closes a block when the NEXT one opens still gets the right answer —
the last constraint is closed by `editparams` opening. Remove the final close and no shipped file
reddens. The format does not require that trailing block, so the line is not dead; it needed an
authored specimen ending exactly on its last `ragdollconstraint`. See
[[author-the-specimen-the-corpus-lacks]].

**Invariant culture or every ragdoll is rigid.** A `.phy` writes `"-35.000000"`; parsed under a
comma locale every joint limit reads zero, silently, on somebody else's machine.

## The control that says the text was found at all

The header's `solidCount` counts HULLS in the closed section; the text counts `solid` blocks. They
describe the same bodies from opposite ends of the file, so asserting they agree catches a text scan
that landed at the wrong offset — which otherwise produces a plausible list of the wrong length and
no error. They agree on every class model.

**What is still not readable: the hulls**, which is what a falling body contacts the world with.
`volume` is given per solid, which is not a shape.
