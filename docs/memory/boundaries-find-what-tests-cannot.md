---
name: boundaries-find-what-tests-cannot
description: Drawing an architectural boundary surfaces defects before any test runs, because a test checks behaviour within a structure while a boundary questions the structure.
metadata:
  type: project
---

**Separation of concerns is worth it for testability, and the act of DRAWING the boundary pays out
before a single test is written.**

The user's framing, confirming the MVP decision after watching it land:

> the bugs you found and extra things you have been able to test are one of the big upsides of MVP,
> its separation of concerns, which enables testability

**The sharper version, measured on Tf2DemoSalvage 2026-08-22:** of four defects surfaced by the
restructure, only one came from the new tests. The other three came from drawing the line.

| Surfaced by | Defect |
|---|---|
| **Writing** `IPlaybackView` | `TransportBar.Playing`'s setter raised its own change event, so a presenter assigning it re-entered its own handler. Invisible while form and control were one tangle. |
| **Extracting the scene layer** | `WorldVertex`, `WorldBatch` and `SunLight` — pure data — were declared inside the renderer. And `MessageQueue`/`ForegroundProbe` P/Invoked `user32.dll` from what was meant to be a portable project. |
| **Extracting the render layer** | A gap marker's control named a type the renderer never consumed, so its claim had become unfalsifiable. |
| The new tests | 16 playback rules that had no coverage at all. |

**Why this happens, and it is not luck.** A test asks whether the code behaves correctly *inside the
structure it has*. A boundary asks whether the structure is right — so it reaches defects that are
invariant under every test you could write against the old shape. The re-entrancy bug had no failing
input: the form simply never assigned `Playing` from a path that could re-enter, so no test over the
old code could have gone red.

**How to apply:**

- **Expect the extraction itself to find things**, and treat what it finds as findings rather than as
  refactor friction. The compiler pointing at a misplaced type is information about the design.
- **Writing an interface is an inspection, not paperwork.** The rule you have to state — "setting
  this must not raise that" — is the moment you check whether the real implementation obeys it.
  Three of these came from having to write something down.
- **Do the smallest concern first and wire it before extracting more.** The pattern is proven end to
  end on one, and a design flaw found on the sixth presenter costs six.
- Related: [[a-faithful-fixture-can-be-blind]] and
  [[output-level-assertion-or-it-is-not-done]] — different instruments, same theme, that a check
  only sees what its shape allows it to see.
