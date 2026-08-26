---
name: a-log-level-regression-is-invisible-to-unit-tests
description: A recording logger counts messages, not levels, so LogDebug to LogTrace stays green while the app goes silent.
metadata:
  type: reference
---

**Change `LogDebug` to `LogTrace` and the unit suite does not notice.** Measured 2026-08-26 on
`FrameReporter`: 8/8 green, while the running viewer stopped writing its per-second frame line
entirely.

**Why:** a test double like `RecordingLogger` implements `IsEnabled` as `true` for every level and
records `(Level, Message)` pairs. Assertions are written as `log.Count("frames a second")`, which
matches on the message. The level is captured and never asserted, so it is free to change.

The real application is the opposite: it runs at a configured minimum level — here `+developer 1`,
which maps to `Debug` — and anything below it is discarded before the message is formatted. So the
level is the ONLY thing that decides whether the line exists, and it is the one thing the unit test
does not look at.

**What makes this worth its own entry is what it costs.** The line lost this way was the instrument
[[logs-are-the-debugger]] describes — the one B191 (a log line taking a machine-wide mutex) and B163
(freezes with no frame-rate drop) were both found with. Losing a diagnostic is silent by
construction: nothing fails, the log is just quieter, and the next investigation starts without it.

**How to apply:** for any log line that is an INSTRUMENT rather than a note, assert it at the third
level — the real application, launched, with its real log configuration. That is the only place the
level is real. `WiringUiTests` is where those live in this repo.

**The related trap, same session:** deleting a call outright often will NOT compile, because the
arguments the call site builds become orphaned and the analyzers report them (S1144 unused method,
S4487 unread field). That is a genuine structural guard — but it only covers the crudest regression,
and reaching for it as reassurance is how the subtler one survives.

Related: [[three-test-levels-and-the-third-is-missing]],
[[output-level-assertion-or-it-is-not-done]], [[a-log-must-name-what-it-measured]].
