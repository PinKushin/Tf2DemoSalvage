---
name: a-whole-timeline-can-leak-the-future
description: "FIXED by B548: track scalars were last-write-wins (Econ, ModelPath...) and answered every tick with the demo's END value; a window disagreed because it was right"
metadata:
  node_type: memory
  type: project
---

**Fixed by B548** (`HeldValues<T>`: prop facts held per tick, trimmed with the rest of a track). Kept for the
pattern: a whole-build reference can itself be wrong.

`ScenePropTrack.Econ`, `ModelPath` (via `Follow`), `ItemDefinitionIndex`, `ClassName`, `TeamNumber` and the other
track scalars are overwritten as the builder walks, so a WHOLE timeline answers every tick with the value the demo
ended on. A window (D217 step 3: a checkpoint decoded forward to just past the tick) answers the value as of then —
which is what the engine holds.

**Why it matters:** the window equivalence (`timeline-windows`) treats the whole timeline as the reference. On f12 and
rgl-pug its only remaining differences (16 and 6 of 80 seeks) were these fields, and they are the reference's fault,
not the window's.

**How to apply:** when a window/whole comparison differs in a track scalar, check whether the field is last-write-wins
before debugging the window. Making them per-tick histories changes the whole timeline's answers — name the trade to
the owner first (`name-the-trade-before-fixing-valve.md`). Filed in D217 step 3.
