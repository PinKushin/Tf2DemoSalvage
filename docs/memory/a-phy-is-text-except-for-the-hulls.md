---
name: a-phy-is-text-except-for-the-hulls
description: Masses, bone names, joint limits and friction are plain KeyValues in a .phy; only the Havok hulls are closed — and every shipped file ends with a trailing block that hides a reader bug.
metadata:
  type: project
---

**"We'd need a physics engine to read a `.phy`" is wrong** — this entry exists because that
conclusion was reached once already. The file is: `phyheader_t` (`int size; int id; int solidCount;
int32 checkSum;`, 16 bytes, `phyfile.h:14-21`); `solidCount` collision hulls in Havok's closed IVPS
format; then plain-text KeyValues carrying everything else.

`PhysicsModel` in `Tf2DemoSalvage.Content` reads the header and the KeyValues: per solid the index,
**bone name**, parent, surfaceprop, mass, inertia, damping, volume, drag; per joint the parent,
child, three axes of min/max/friction. Only the shapes are missing. `name` is load-bearing —
constraints refer to solids by INDEX, so without bone names the joint graph is numbers over an
unknown ordering.

Engine dispatches on the same block names (`solid`, `ragdollconstraint`,
`ragdoll_shared.cpp:283-293`); field names from `solid_t` (`vcollide_parse.h:16-24`),
`objectparams_t` (`vphysics_interface.h:1062-1075`), `constraint_axislimit_t`
(`constraints.h:61-79`) — file's `friction` is Valve's `torque`. Every class tree has one fewer
constraint than solids (demo/pyro 15/14, heavy 16/15, scout/sniper 17/16, engineer 18/17, medic
24/23); heavy's solids total **102.0 kg**.

## Two traps, both found by sabotage

- **A trailing block hides a missing block-close.** Every shipped `.phy` ends with an `editparams`
  block, so a reader that closes a block only when the next opens still passes — the final
  constraint closes via `editparams` opening. An authored specimen ending exactly on its last
  `ragdollconstraint` is needed to catch it. See [[author-the-specimen-the-corpus-lacks]].
- **Invariant culture, or every ragdoll is rigid.** `"-35.000000"` parsed under a comma locale reads
  zero, silently.

## The control

`solidCount` (header) counts hulls; the text counts `solid` blocks — same bodies from opposite ends
of the file. Asserting agreement catches a text scan landed at the wrong offset (they agree on every
class model).

**Still not readable: the hulls** — what a falling body contacts the world with. `volume` is given
per solid, which is not a shape.
