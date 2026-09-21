---
name: a-measure-needs-focus
description: do not compare fps unless the owner asks for performance work; the viewer is often backgrounded while he uses the machine, so any frame number is suspect
metadata:
  type: feedback
---

**Do not compare frame rates unless the owner has asked for performance work.** The owner, 2026-09-21: *"you shouldnt
compare fps normally, only when i tell you to focus on fps really, because sometimes, expecially when im on the computer
with you, the app is going to background. it takes time to load thats time for something to pop up in front of it."* He
was clear that this is not a rule he set, and no rule of his had asked for fps checks. The habit was the assistant's own:
on 2026-09-21 it read "93 fps" off a `--shot` capture and spent five viewer runs on a camera-column comparison. Every one
of those runs was backgrounded, so the comparison measured nothing.

A backgrounded viewer is throttled by `engine_no_focus_sleep` (50 ms, `FramePacer.NoFocusSleep`). The frame breakdown
leaves out the sleep, so the loss looks like unexplained time. On 2026-09-18 two f12 runs dipped to ~19 fps at 50 ms with
every column near zero; a run that kept focus held 299 fps. The owner said then: *"i was watching youtube over top of you"*.
He uses the desktop while the lock is held, because the lock only binds agents.

**Why:** a load takes long enough for any window to land in front of the viewer. So a frame number taken during ordinary
work is suspect by default. It also costs viewer runs that take the desktop from him.

**How to apply:** ignore the fps overlay on captures and the frame lines in `--shot` logs during feature work. Use
`--measure` only when the owner asks for performance. Even then, confirm focus was held before believing a drop: the 50 ms
signature with near-zero columns means focus was lost. Run a control with the change disabled before chasing a cause;
[[instrument-bugs-outnumber-decoder-bugs]], [[debug-is-what-the-owner-runs]].
