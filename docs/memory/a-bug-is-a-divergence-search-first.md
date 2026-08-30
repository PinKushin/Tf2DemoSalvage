---
name: a-bug-is-a-divergence-search-first
description: Our bugs are not new problems; spend the opening effort finding which Source mechanism we are missing, not debugging our own invention.
metadata:
  type: feedback
---

**The owner, 2026-08-30**, after a fix shipped and had to be pulled:

> *"diversions from valve cause issues like this, any bug we find should be a diversion search for
> the first like hour"*

> *"not a rule, just a kinda standard in a way, its loose i dont expect a real timed hour, the point
> is that none of our issues are not solved problems within the source engine, we have no reason to
> not use those answers, and by not using those answers we run into bugs and compatability issues"*

**Why:** this is a viewer for Valve's format, reading Valve's maps, drawing Valve's models. Anything
that looks wrong is something the engine already does right, so the opening question is *which
mechanism are we missing or doing differently* — not *what is wrong with our code*. The hour is a
posture, not a stopwatch.

**How to apply, and the failure mode is stopping too early.** B231 found a real, cited divergence —
`C_BaseEntity::ShouldDraw` refuses `kRenderNone`, and every `func_door` on `cp_fulgur` carries it —
implemented it, and deleted the map's gates. The search had answered "does the engine draw this
entity" and never asked "then what DOES draw the gate". The answer sat in the same entity lump:

```
func_door 'setupgate_stage1_1_bottom'  rendermode 10   <- invisible mover
  prop_dynamic door_grate003_bottom.mdl parentname 'setupgate_stage1_1_bottom'
```

Every gate is an invisible mover plus a **parented** visible prop. The door brushwork we should not
have drawn was standing in for the grate we were not drawing.

So: read until the mechanism is **whole**, not until a line of C++ agrees with you. A citation that
explains why something should be hidden is half an answer; the other half is what the engine shows
instead. Related: [[half-a-mechanism-is-not-parity]],
[[read-the-sdk-for-the-whole-mechanism]], [[decoding-a-field-is-not-honouring-it]],
[[valve-parity-is-the-first-principle]], [[ask-valve-before-designing-not-after]].
