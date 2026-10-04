---
name: an-event-is-taken-not-read
description: "A per-frame event list cleared when TIME moves but read every FRAME replays while paused — B473, 27,882 footsteps at one tick. Consumers take events; pause is the test condition."
metadata:
  type: feedback
---

B473: `EntityModels.FiredEvents` cleared on demo-time change, read by the sound pass on every camera upload. Paused =
time frozen, frames continue = same footsteps every frame. Unit suite green; found only in a UI log tail.

**Why:** engine fires an event once (`DoAnimationEvents`); a list outliving its frame is a second firing.

**How to apply:**
- Producer cadence != consumer cadence: hand events over by TAKE (move + clear), never a readable property.
- Take before any early return, else a skipped frame's events sound late.
- Paused is a test condition, like 8x speed ([[play-fast-to-find-lifecycle-bugs]]): count a log line across paused
  frame-rate reports; it must not grow.
- Before filing "engine walks every frame", read the caller's guard: `C_BaseAnimating::Simulate` walks only when
  `gpGlobals->frametime != 0` — a suspected second divergence died on that line (recorded in B473).
