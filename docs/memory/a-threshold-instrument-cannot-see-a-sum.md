---
name: a-threshold-instrument-cannot-see-a-sum
description: Six frames froze on sound decode while the per-decode stall log fired once; time the PHASE, not the event.
metadata:
  type: project
---

**A per-event instrument with a threshold is blind to accumulation.** `Sample()` logged
`STALL decoding '<name>' took N ms` when a single decode passed `StallSeconds` (0.03). Measured
2026-08-25 on cp_process: **six of eleven slow frames were dominated by the sound step at 27–91 ms,
and exactly ONE decode stall was logged.** A frame that starts three sounds pays three decodes that
each fall under 30 ms, so the event instrument reported almost nothing while frames visibly froze.

The frame **ledger** saw it immediately, because it times a *phase* between two timestamps and
prints every bucket plus an `unaccounted` residual:

```
SLOW FRAME 99 ms: sound 90.7, camera 0, project 0, advance 6.2, capture 0, hud 0, draw 1.7
```

**Why:** a threshold answers "was any ONE occurrence expensive". The question a stall asks is "was
this FRAME expensive, and where did it go" — a different question, and no per-event threshold can
be tuned into answering it. Lowering the threshold makes it log constantly without ever summing.

**And this was a repeat.** B163's commit message already said: *"No counter named it, because it
sits outside both `_posingTicks` and `_drawTicks`. Every performance investigation had been reading
numbers structurally incapable of seeing it."* This session then spent its whole first half
optimising `posing` — the exact counter named there — and bought ~20 ms of 545 while the real cost
sat in a bucket nothing was reading. The lesson had been written down and was not applied.

**How to apply:**

- **Read the phase ledger FIRST**, before optimising any named counter. If `posing` and `draw` read
  1.7–2.6 ms on the slow frames, the stall is not in posing or drawing, and no amount of work there
  will move it.
- **The residual is the important column.** A large `unaccounted` says the cost is somewhere nobody
  has thought to measure, which is where every one of these has been found.
- When adding a stall log for an operation that can happen several times per frame, **accumulate
  per frame and report the total**, or accept it can only ever catch the single worst case.
- Ask the owner what the previous fix actually was — "check the commits where we fixed the stutter
  and hiccup the last time" located this in one step after a long stretch of guessing.

Related: [[measure-the-output-not-the-capability]], [[a-log-must-name-what-it-measured]],
[[instrument-bugs-outnumber-decoder-bugs]], [[measure-every-hop-before-blaming-one]],
[[logs-are-the-debugger]].
