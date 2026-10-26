---
name: count-the-changes-before-caching-on-unchanged
description: "Before building a skip-if-unchanged cache, count how often the input actually stays the same in one real run; TF2 faces change every render frame, not every tick."
metadata:
  node_type: memory
  type: feedback
---

**Count what changes before caching on "unchanged".** B597's face cache (2026-10-26) was correct and identity-tested,
and it saved nothing: under 1% of faces on f12 held still from one frame to the next. It was reverted on the owner's
decision (D236).

The prediction (30-70% held) assumed the weights change at the tick rate. They are evaluated at the continuous render
time — the interpolated `m_flexWeight`, the blink, and `RunFlexDelay` on `frametime` — and TF2's idle scenes animate
the face continuously. The engine recomputes on every draw too (`c_baseflex.cpp:1147-1156`).

**How to apply:** first add a count-only instrument at the point where the decision would be made, and take one viewer
run. That costs minutes; the cache cost a branch, a grid and a revert. A synthetic test that poses twice at the same
time will show "held", and that is not the production cadence.

**What paid instead was the bytes per face:** the flexed range (measurements/2026-10-26-b597-flex-range.md).
