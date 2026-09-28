---
name: logs-are-the-debugger
description: No debugger here, so logs must report state and decisions — what a log must say, what a wrong log costs, and why only the running app can prove one still exists; plus an optional dependency's null-object default silencing a forgotten wiring, a background launch's "completed" notification describing the wrapper rather than the app, and when to close a viewer you booted yourself.
metadata:
  type: feedback
---

No debugger exists here. Logs must carry **what the code decided and what it was working with**, not
just what went wrong. Owner: *"logs are the only way you can watch variables and actually get the
information I could get from a debugger."* Said after an hour spent finding that 42 of 189 cp_process
materials declare `$envmap`, unimplemented, with nothing logged because nothing FAILED — a control
point drew as a black disc in silence. One line of startup log fixed it and surfaced a bigger gap
(`$vertexcolor`/`$vertexalpha` on 55 materials) nobody had suspected.

## Every subsystem's log states FOUR things

Owner: *"you have to know whats being spat out, whats needed, what we have, and what we need, all
need logs."*

| Category | The question it answers |
|---|---|
| ASKED FOR | what the file wants |
| HAVE | what was found and read |
| PRODUCED | what came out the far end |
| MISSING | absent, unimplemented, or REFUSED — count each kind apart |

Conflating MISSING's kinds is its own bug — "never compiled" and "exists but unused" are unrelated
events sharing one `null`, which cost four wasted hypotheses once. Prefer one line stating a whole
picture over one per event; name individual items for MISSING, since a count says something's wrong
and a name says what to look at.

## READ the logs already being written, before adding more

A viewmodel bug got four rounds of new instrumentation while the renderer was already printing the
exact diagnosis on every frame, with a source comment describing the bug written before it happened.
**Diagnosis starts by reading existing output, not writing new output.**

---

## `log-what-is-about-to-be-drawn` — and run the app before the suite

**Run the app before running the suite** — a 15-second launch has caught defects the multi-minute
suite couldn't. Only works if the log states what's about to be drawn: `roster: 6 red, 6 blu... 12
players drawn` turns "every player is grey" from an eye-catch into a number obviously wrong on sight.

---

## `a-log-must-name-what-it-measured` — a wrong log is worse than none

**A log measuring the wrong quantity is trusted exactly as much as one measuring the right one.**
Repeated failures: "`material not found`" fired when the texture, not the material, failed to
resolve, sending an investigation into path joining that was fine; "`baked frame 0 of 1`" printed for
every skinned player regardless of motion; a speed line was really the probe's own filter threshold; a
seam probe indexed frames a model doesn't have and CRASHED the viewer.

**Name the quantity in the message; say which case you're in when one line serves two.** A wrong log
ends an investigation; an absent one invites a measurement.

---

## `a-log-level-regression-is-invisible-to-unit-tests`

**Change `LogDebug` to `LogTrace` and the unit suite doesn't notice.** A test double implementing
`IsEnabled` as always-`true` matches on message text; the level is captured and never asserted, so
it's free to change, while production discards anything below its configured minimum before
formatting. **The level is the ONLY thing deciding whether the line exists, and the one thing the
unit test ignores.** For any log line that's an INSTRUMENT, assert it at the real application level,
launched with real config.

---

## `a-null-object-default-hides-a-missed-wiring`

**Optional dependencies with null-object defaults** (`ILogger? log = null` → `NullLogger.Instance`)
make a forgotten argument produce NOTHING, with a green suite. D83: converting 193 call sites to
injected `ILogger`, the viewer logged 13 `assets` lines and zero warnings against 215/16 before —
every test passed, the whole 3,231-test gate was green. Three production calls simply weren't passed
the factory.

**Why no test caught it:** tests construct types directly and deliberately pass no factory (want
geometry, not commentary) — the exact wrong shape production used is the shape every test uses. A
unit test proves the component logs when handed a logger; it says nothing about production handing it
one.

**Found by:** launching the viewer, comparing category counts against a pre-change log.

**How to apply:** verify the production call site directly — only the real artefact can. Prefer a
required parameter where production always has the dependency; where optional is right, comment that
omitting it is silent and check output once.

**Repeat instance:** a `PropModels.Load` optional logger, commented "most callers are tests" — there
was exactly ONE caller in the whole repo, and it passed nothing, muting an entire static-prop path
since it was written. **Count the callers before writing that comment.** The log LOOKED populated
(a different path, 20 lines away, had a real logger) — a grep for "did the subsystem say anything"
answers yes while half is silent; ask "did THIS call site's lines arrive".

---

## `a-launch-notification-is-not-an-exit`

**Launching the viewer via `run-exclusive.ps1` as a background task reports "completed" while the
app is still running** — the notification describes the wrapper, not the app. Compounding: reading
the log after that notification shows a file mid-load, since the app is still writing it (one such log
grew from 860 lines to 79MB over a session). Concluding "it exited, you saw nothing" to someone
watching the running window spends credibility.

**How to apply:** ask the OS for the process (`Get-Process -Name tf2demoview`), never the task
notification or the log's last line. If the log must be the instrument, read it twice — growth is the
signal.

---

## `close-what-you-launched`

**Close a viewer launched on your own initiative once it's answered its purpose.** Owner: *"if you
boot if yourself shut it down when your done."* Leaving it holds the exclusive lock and locks build
DLLs, failing the next build with a misleading `MSB3027`. **When the owner asked for the launch, leave
it running.** An unexpected exit is worth a look — check the tail of the viewer log for a tidy vs.
mid-sentence ending.

**On how much to write down** (owner): *"getting all the nuance of situations can be a pita to try to
write down... the best thing to do IMO, is to basically always hedge, id rather understate things than
overstate them."* Prefer the understated version — a rule stated as absolute gets applied confidently
where it wasn't meant to.

Related: [[measure-the-output-not-the-capability]], [[instrument-bugs-outnumber-decoder-bugs]],
[[output-level-assertion-or-it-is-not-done]].

---

## `a-crash-in-dispose-reaches-no-handler` — log BEFORE the step, not after it (B402, 2026-09-12)

**`Dispose` runs inside the window procedure handling close, so an exception leaving it becomes
`0xC000041D` with empty stderr and NO managed handler reached** — registering `ThreadException`/
`UnhandledException`/`UnobservedTaskException` all together logged none of them. That silence IS
the diagnosis: it rules out every managed route at once.

**Two traps building the instrument:** `ThreadException` never fires unless
`Application.SetUnhandledExceptionMode(CatchException)` precedes it; a marker WRITTEN AFTER a step
can't name the step that killed you — **log a line BEFORE each step instead**, so the last line in the
file is the culprit.

**The fault generalises: disposing a control raises `Resize`** — WinForms destroys the window on the
way down, and any still-attached handler runs against what's already released. **Unhook a control's
own events before releasing anything.**

Related: [[instrument-bugs-outnumber-decoder-bugs]],
[[ci-is-the-machine-without-tf2]]#read-ci-before-pushing-onto-it,
[[port-the-engines-bottom-layer-first]]#retaining-what-the-engine-deletes-breaks-its-invariants.
