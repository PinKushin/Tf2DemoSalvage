---
name: parity-includes-how-the-engine-schedules-work
description: Parity audits must cover how the engine SCHEDULES work (threads, queues, pools), not only what it draws; this was missed until 2026-10-22 and may force a large rewrite.
metadata:
  type: feedback
---

Owner, 2026-10-22, after finding 98 showed TF2 renders on a second thread (`mat_queue_mode`) while the viewer is serial: *"note this, since it wasn't obvious to us from the beginning like it should have been, this might end up being a massive rewrite"*. Earlier: *"I thought we had been parallelizing shit. that was stupid to not do"*, *"we should be threading the hardest heaviest stuff, not the easiest"*, *"if TF2 is booting and arming workers, we probably should too, that way we don't have to spin them up to parallelize, we just throw at it what we want to run"*.

**What happened.** Every parity audit asked "what does the engine draw and simulate". None asked "on which thread, in what order, through which queue". The per-frame path (advance, camera, draw, HUD) has always run on one thread; the only off-thread work was the load worker, the corpse worker and (since 2026-10-22) sound loads. The fps gap to TF2 on f12 stayed 2.4-2.8x after findings 89-95, with no single cost above 3.2% of the render thread: the shape of a scheduling difference, not a list of slow functions.

**How to apply.**
- A parity audit of any subsystem now includes its threading: what runs where, what is queued, what blocks the game thread, how many frames in flight (finding 98 is the model; D228 is the approved first step).
- Thread the HEAVY work the engine threads, not the easy work: finding 94's particles were chosen because the audit listed them as independent and they were the smallest piece (0.08 ms saved).
- The engine starts a fixed worker pool at boot and throws jobs at it; copy that shape (persistent pool, spin-then-sleep, engine-like worker count) once a first real job needs it; do not build the pool alone.
- Expect this to be large: the viewer's frame is built as one serial chain, and a queued render thread needs a frame snapshot/hand-off. Say so in plans; do not promise small steps.
- Measure the engine's threads before deciding what to move (per-thread CPU of TF2 vs the viewer, and the 2-core affinity test, 2026-10-22).

Related: [[valve-parity-is-the-first-principle]], [[observed-not-deduced-and-test-what-you-decide]], [[a-parity-rule-may-bound-the-cost]].
