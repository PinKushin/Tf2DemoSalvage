---
name: every-densifying-step-needs-the-delta-flag
description: Any blend that fills in bones neither side lists must be told whether the poses are deltas; there were two such steps and only one knew.
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-03T19:17:50.475Z
---

**A delta pose passes through more than one expansion, and every one of them has to seed the same
way.** `CalcVirtualAnimation` (`bone_setup.cpp:933`) makes the choice once — a delta's unlisted bone
is identity and zero, an ordinary animation's is the bind pose — and each later step that names
every bone repeats that choice or destroys it.

`SkinnedModel.Locals` has **two** such steps and B284 fixed only the first:

1. the FRAME blend, between two frames of one animation — told;
2. the GRID blend, across the up-to-three corners of a blend grid — **not told**, because it called
   a four-argument overload that quietly meant `additive: false` (B298, 2026-09-03).

**Every TF2 player's aim matrix is a delta blend grid**, so this was not an edge case:
`PRIMARY_aimmatrix_idle` is 3x4, delta on the sequence and on the animation, reached from
`stand_PRIMARY` by autolayer. Seeded from the bind pose, its root came back as a 63° rotation and a
14-unit offset instead of a near-identity difference, and `QuaternionMA` added that over the body at
full weight. Seven of fifteen players stood on their heads.

**The convenience overload is deleted, not documented.** `additive` is a required argument now. It
had a three-paragraph doc comment explaining the exact branch it was getting wrong, which is the
argument against comments as a guard — see [[a-divergence-is-asked-not-documented]].

**The defect was older than the symptom.** Nothing reached a delta grid as a LAYER until autolayers
were wired the same day, so the wrong seeding had nothing to add itself to. When a visual bug
appears right after a change, the change may only have made an existing fault reachable — see
[[a-bug-is-a-divergence-search-first]] and [[a-delta-animation-is-not-a-pose]].
