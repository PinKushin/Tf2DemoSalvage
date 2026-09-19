---
name: a-measure-needs-focus
description: a --measure run's fps is only valid while the viewer holds focus; ~19 fps at 50 ms with every column near zero is engine_no_focus_sleep, not a regression
metadata:
  type: feedback
---

A `--measure` run that drops to ~19 fps, 50-53 ms a frame, with every measured column near zero, is `engine_no_focus_sleep`
(50 ms, `FramePacer.NoFocusSleep`) — the viewer lost focus, not a regression. On 2026-09-18 two f12 runs dipped that way; a
control run with the suspected change disabled dipped the same, and a run that kept focus held 299 fps. The owner confirmed:
*"i was watching youtube over top of you"* — he uses the desktop while the lock is held, since the lock only binds agents.

**Why:** the frame breakdown excludes the sleep, so the dip looks like unexplained time and invites a hunt for a cause in code.

**How to apply:** before blaming code for a frame-rate drop in a measurement, check whether focus was held — the 50 ms signature
with near-zero columns names it at once. Run a control (same command, change disabled) before chasing it; [[instrument-bugs-outnumber-decoder-bugs]].
