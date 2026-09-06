---
name: a-gate-in-flight-owns-the-tree
description: Editing a source file while the gate is running invalidates the run silently; early projects measured the old tree and later ones the new.
metadata:
  type: feedback
---

**The gate builds and tests one project at a time, so a source edit part-way through splits the run
in two.** Done on 2026-09-06: `EntityState.cs` was edited while `build/gate.sh` was between
projects. `core.trx` was written at 16:15 and the edit landed at 16:16, so **Core was measured
against the OLD tree** while `scene`, `audio`, `presentation`, `content` and `corpus` rebuilt Core
as a dependency and were measured against the NEW one. `DemoTimeline.cs` then changed at 16:20,
mid-corpus.

**The run exited 0 and every count was above its floor.** That is the whole problem: nothing about
the output says it measured two different trees, and a green gate is exactly what is used to decide
a merge.

**Why:** the gate's value is that it is one measurement of one tree. Editing under it produces a
result that is neither a measurement of what was there before nor of what is there now, and it fails
in the direction that matters — it can pass while the current tree is broken, because the project
that would have caught it ran before the change.

**How to apply:** while a gate is in flight, do documentation, reading and planning — never a source
edit. If one happens anyway, say so and re-run rather than quietly using the result; the run is not
evidence about the tree that exists. This is the same rule D145 states for subagents from the other
side — *"the parent does not build or measure while one holds a source file"* — and the same family
as [[insert-below-the-member-not-above-it]]'s note that a build break now costs whatever else is
running. Related: [[read-the-trx-total-not-the-console]].
