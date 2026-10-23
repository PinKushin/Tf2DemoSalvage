---
name: parity-and-fps-gains-need-no-ask
description: Parity fixes and fps increases are pre-approved by the owner; don't ask "want me to start it?" - report the cost honestly and proceed (visible-behaviour changes still need his say).
metadata:
  type: feedback
---

Owner, 2026-10-22, when asked whether to start the double-buffered render-thread rewrite (about 15% for a big careful change): *"yes start it, I'm never saying no on parity and fps increases, I really want better than TF2 fps, because Valve are geniuses but TF2 is a 10+ year old game with a massive amount of unoptimized updates"*.

**Why:** the goal is to beat TF2's fps, not to match it; the gap is large (2.4-2.8x on f12) and he will not trade it for effort.

**How to apply:**
- A measured parity fix or fps gain does not need a "want me to start it?" round trip. Say what it costs and expected size (labelled estimate), then do it, checkpointed, shipped off or behind a switch when risky.
- Still ask when the change alters something he can SEE (latency, colours, behaviour) or the engine itself is ambiguous.
- Still stop at an agreed checkpoint when the measured gain does not justify the work, and report; his "yes" covers gains, not work for nothing.

Related: [[valve-parity-is-the-first-principle]], [[parity-includes-how-the-engine-schedules-work]], [[how-to-prioritize-the-work]].
