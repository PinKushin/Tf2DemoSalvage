---
name: logs-are-the-debugger
description: "No debugger here, so logs must report state and decisions — a failure-only log reads clean while everything falls back."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-14T01:02:12.869Z
---

There is no debugger in this environment. Logs are the only way to watch a variable, so they have
to carry **what the code decided and what it was working with**, not only what went wrong.

**Why:** the owner said it directly — "you don't have or are simply not using a debugger so logs are
the only way you can watch variables and actually get the information I could get from a debugger."
It was said after watching an hour go into finding that 42 of 189 materials on cp_process declare
`$envmap`, which the renderer does not implement. Nothing was logged the whole time, because
nothing *failed*: every material resolved, every texture decoded, and a control point drew as a
black disc in silence. The fix was one line of startup log that states what the map asked for, and
its first run also named `$vertexcolor`/`$vertexalpha` on 55 materials — a bigger gap nobody had
suspected, sitting in VMTs that had already been read aloud and not noticed.

**How to apply:** when something cannot be explained, add the log before adding the hypothesis. A
report built only from failures reads clean while every instance quietly falls back, so log the
census, the count, the chosen branch, the resolved value — the things a breakpoint would show. Log
the *absence* of an expected event too. Prefer one line stating a whole picture ("48 unimplemented
parameters across 189 materials: ...") over a line per event, which is unreadable at map scale.

Related: [[measure-the-output-not-the-capability]] is the same failure seen from the reporting side,
[[instrument-bugs-outnumber-decoder-bugs]] is why the log itself needs checking before it is
believed, and [[log-what-is-about-to-be-drawn]] is this rule applied to the renderer.
