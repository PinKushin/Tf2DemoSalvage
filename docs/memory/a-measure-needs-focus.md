---
name: a-measure-needs-focus
description: do not compare fps unless the owner asks for performance work; the viewer is often backgrounded while he uses the machine, so any frame number is suspect
metadata:
  type: feedback
---

**Do not compare frame rates unless the owner has asked for performance work.** Owner, 2026-09-21:
*"you shouldnt compare fps normally, only when i tell you to focus on fps really... the app is going
to background. it takes time to load thats time for something to pop up in front of it."* This was
the assistant's own habit, not a rule he'd set — it read "93 fps" off a `--shot` capture and spent
five backgrounded viewer runs comparing camera columns; all measured nothing.

A backgrounded viewer is throttled by `engine_no_focus_sleep` (50ms, `FramePacer.NoFocusSleep`), a
loss the frame breakdown doesn't show as sleep. Two f12 runs dipped to ~19fps at 50ms with every
column near zero; a focused run held 299fps. Owner: *"i was watching youtube over top of you"* — the
lock only binds agents, not him.

**Performance has its own step, after the feature.** Owner: *"fps is important... implementing
everything to valve parity, then checking our performance by having me watch the f12 demo, and then
proping for fps gains... we normally have made small mistakes which cost fps, but are easily fixed if
we get them right after the imp is steady and working."* Build to parity, hand to f12 review, then
probe for gains — note a suspected cost during the build step rather than chasing it mid-feature.

**Repeated 2026-10-01, and it cost a subagent.** Gate 3 (`playback-check`) on a cut ESEA demo read
"1.5 fps, `project` 574 ms/frame"; the assistant filed it as a viewer defect and launched an opus agent
on it. Owner: *"OMG, you put a subagent on a FPS problem, the fucking viewer was backgrounded, the
frames for gate 3 dont matter its always backgrounded"*. Gate 3 proves a demo plays and exits — its
frame numbers are never evidence of anything. Owner: *"that was 200k tokens wasted, dont do that again"*.
Before launching ANY agent on a performance symptom, check this file; a gate-3 number never qualifies.

**How to apply:** ignore fps overlays/`--shot` frame lines during feature work. Use `--measure` only
when asked, and confirm focus was held first (50ms signature + near-zero columns = focus lost). Run a
control with the change disabled before chasing a cause. [[instrument-bugs-outnumber-decoder-bugs]],
[[debug-is-what-the-owner-runs]].
