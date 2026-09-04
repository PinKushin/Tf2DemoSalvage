---
name: measure-the-gate-before-building-the-branch
description: A conditional's famous branch can be reached almost never — count how many of your own inputs pass the gate in front of it before building it, or you implement a 1% case.
metadata:
  type: feedback
---

**Before implementing a branch, count how many of this project's own inputs actually reach it.** A
memorable engine mechanism can sit behind a gate that almost nothing passes, and nothing in the code
says so — the gate is usually a `switch` or an early `return -1` several functions away.

TF2's corpse death animation was the case. The famous line is the coin flip:

```cpp
if ( !m_bIceRagdoll && !tf_always_deathanim.GetBool() && (RandomFloat( 0, 1 ) > 0.25f) )
    iDeathSeq = -1;
```

which reads as "a quarter of deaths play an animation" and got recorded that way. The gate in front
of it is `GetSequenceForDeath` — a `switch` on `m_iDamageCustom` with two cases and **no default**
(`tf_player_shared.cpp:13441-13455`). Only headshots, decapitations and backstabs are eligible;
everything else returns -1. Counted on real demos:

| demo | corpses | eligible |
|---|---|---|
| comp 6v6 | 159 | **0** |
| koth pub | 457 | 22 |
| koth pub | 147 | 5 |

**About one corpse in a hundred.** A day spent on it would have changed nothing anybody could see,
and the audit's own entry — which said "three quarters of ELIGIBLE deaths" — was literally true and
read as though eligible meant most of them.

**The measurement is cheap and it is the same probe you already have.** Decode the field the gate
switches on and count. Here it also produced the prioritisation: what actually lays a corpse down is
the physics, which every corpse takes.

**And the zero needs a control**, because a field decoding to its default looks identical to a real
absence ([[an-empty-search-needs-a-control]]). The spread settles it: the comp match's values were
`NONE`, `STANDARD_STICKY`, `ROCKET_DIRECTHIT`, `AIR_STICKY_BURST` — a soldier-and-demo match exactly,
with no sniper or spy to produce an eligible death. The pub demos, which field both, are where the
eligible ordinals appear at all.

**The same question with the answer the other way, an hour later.** `STUDIO_PROC_QUATINTERP` is
declared, named and computed by nothing — four bones on the player models in one demo. Four out of
540 sounds like nothing. The question that settles it is not the count but **whether any vertex is
weighted to those bones**, because a procedural bone nothing is skinned to computes a transform that
reaches no mesh. The `bone-flags` probe was taught to say `SKINNED` or `no-verts` for exactly this,
and all four came back SKINNED — a forearm that does not twist with the wrist. Worth building.

**And the follow-up caught an overclaim in that same sentence.** It first read "on every class
model", which came from a single tick containing a demoman and a scout — the smallest sample that
could produce it. Counted over all 14,109 models the game ships, the rule is on **three** classes,
scout, heavy and demoman. "Every X" from one observation is a guess wearing a quantifier, and it
survived being written into three documents before anyone counted.

So the rule is not "small counts are not worth it". It is **find the quantity that decides whether
anybody can see it, and measure that one** — eligible inputs for a branch, weighted vertices for a
bone, drawn instances for a prop. The raw count answers neither way.

Related: [[the-cited-line-may-be-the-wrong-branch]] (the branch you found is the one you do not
take), [[measure-the-route-before-building-on-it]], [[audit-means-verify-what-exists]],
[[decoding-a-field-is-not-honouring-it]], [[the-denominator-decides-what-can-be-lost]].
