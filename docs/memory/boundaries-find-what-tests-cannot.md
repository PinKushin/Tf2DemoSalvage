---
name: boundaries-find-what-tests-cannot
description: Drawing an architectural boundary surfaces defects before any test runs, because a test checks behaviour within a structure while a boundary questions the structure.
metadata:
  type: project
---

**Separation of concerns pays out for testability before a single test is written.** Owner, confirming
MVP after watching it land: *"the bugs you found and extra things you have been able to test are one
of the big upsides of MVP, its separation of concerns, which enables testability."*

Measured 2026-08-22: of four defects surfaced by the restructure, only ONE came from new tests:

| Surfaced by | Defect |
|---|---|
| Writing `IPlaybackView` | `TransportBar.Playing`'s setter raised its own change event — re-entrant, invisible while form and control were one tangle |
| Extracting the scene layer | Pure data types declared inside the renderer; `MessageQueue`/`ForegroundProbe` P/Invoked `user32.dll` from a "portable" project |
| Extracting the render layer | A gap marker's control named a type the renderer never consumed |
| New tests | 16 playback rules with no coverage |

**Why:** a test asks whether code behaves correctly inside the structure it has; a boundary asks
whether the structure is right, reaching defects invariant under every test writable against the old
shape — the re-entrancy bug had no failing input at all.

**How to apply:** expect extraction itself to find things — treat findings as findings, not friction.
Writing an interface is an inspection ("setting this must not raise that" is the moment you check).
Do the smallest concern first, wire it end to end before extracting more — a design flaw found on
presenter six costs six.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[output-level-assertion-or-it-is-not-done]].
