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

**How to apply: every subsystem's log states FOUR things, and this is the default shape rather than
something to reach for.** The owner had to say it out loud on 2026-08-16 — "you have to know whats
being spat out, whats needed, what we have, and what we need, all need logs" — after watching a
prop-lighting fix land with failure-only logging. That is basic reverse engineering and it should not
have needed saying; the rule was already written on this page and was not applied.

| Category | The question it answers |
|---|---|
| **ASKED FOR** | what the file wants — placements, materials, parameters declared |
| **HAVE** | what was found and read successfully |
| **PRODUCED** | what actually came out the far end — triangles, textures bound, values decoded |
| **MISSING** | what is absent, unimplemented, or REFUSED, each kind counted apart |

The fourth splits further, and conflating its kinds is its own bug: "the compiler never made this"
and "it exists and we would not use it" are unrelated events. `PropModels` returned one `null` for
both, so four refused vertex-lighting files sat inside an ordinary-looking "without baked lighting"
total while B83 spent four hypotheses on the props they belonged to.

When something cannot be explained, add the log before adding the hypothesis. Prefer one line
stating a whole picture ("48 unimplemented parameters across 189 materials: …") over a line per
event, which is unreadable at map scale — but name the individual items for the MISSING category,
because a count says something is wrong and a name says which object to go and look at.

**The other half of the rule: READ the logs already being written, before adding more.** The
viewmodel spent four rounds of new instrumentation being invisible while the renderer printed, on
every frame:

```
WARN [render] a model was posed but the renderer has no geometry for it
```

with a comment above it in the source reading "the renderer's copy of the packed set is older than
the caller's, which draws nothing and reports nothing" — a description of the exact bug, written
before it happened. A past session had anticipated the failure, logged it, explained it, and nobody
looked. **Diagnosis starts by reading the existing output, not by writing new output**; a log added
in preference to one already there also costs the time it takes to write.

Related: [[measure-the-output-not-the-capability]] is the same failure seen from the reporting side,
[[instrument-bugs-outnumber-decoder-bugs]] is why the log itself needs checking before it is
believed, and [[log-what-is-about-to-be-drawn]] is this rule applied to the renderer.
