---
name: ui-tests-run-every-time
description: "The UI suite runs on every change, not just UI ones, and any UI addition gets a UI test."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:34:51.506Z
---

**Run the UI suite every time**, alongside unit and integration, not only when the change looks
UI-shaped. And **anything added to the UI gets a UI test added with it.**

Owner's instruction, given after a session where the UI suite was run twice in several hours of
viewer work.

**Why:** the viewer is the product here, and the defects that reached the owner this session were
all in it — a borderless mode that behaved as exclusive, full screen at one frame a second, props
that drew black. None were caught by the unit suites, which stayed green throughout. The existing
full-screen UI test also opened no demo, so it had no map and could not distinguish the fast build
from the slow one; that was fixed by giving it a demo, which is the same lesson in miniature.

**How to apply:** `run-exclusive.ps1 dotnet test <UI project>` — it takes the desktop, so it holds
the machine-wide lock, and the owner should be told before it starts. When adding a control, a
mode, a menu item, or a rendering path, add the UI assertion in the same change rather than
promising it later. Where the property genuinely cannot be observed from the automation tree — z
order against another application, for instance — say so in the commit instead of substituting a
check on the flag, which only restates the diff. Related: [[tests-before-codecs]],
[[nunit-shared-fixture-is-the-standard]].

## The phase-scoped exception has expired, 2026-08-26

There used to be a companion entry saying the UI suite was optional *while the UI was small*. It is
not small any more — twenty tests, and they have earned their keep: the F11 collision that silently
broke full screen for days (B165), three wiring regressions that shipped at 620/620 green (B193), and
a per-second diagnostic that a log-LEVEL change silenced while its unit tests stayed green. The
exception is closed and this entry is the whole rule.

**The worked example it carried is worth keeping**, because it is the reason a UI test can be worse
than none. `Click_TheCycleTargetButton_ReachesTheSpectatorCode` counted the log line
`"following entity N"` to prove a click reached `CycleTarget`. That line is written only when the
target search SUCCEEDS; the other branch writes `"nobody else to follow at this tick"`, and **both
prove the wiring**. So the test asserted "the click reached the handler" by requiring "and it found
somebody" — a fact about the demo and the tick, not about the code. Once B171 required a target to be
alive and drawn, the solo POV era specimen the UI session opens legitimately produced no target, and
the test went red against a viewer the owner was watching work correctly. His verdict: *"that seems
like a stupid test for a pov demo or a demo with a single player, it doesnt actually check
anything"*.

Fixed by counting the `[spectate]` area instead of either message — which also sharpened the negative
control, since in the free camera `CycleTarget` returns before logging anything, so the count proves
the handler never RAN rather than merely that it found nobody.

---

## `a-shared-viewer-test-restores-what-it-changed` — never depend on running last

**A UI test may change the shared viewer's state, and if it does it restores it. It may not depend
on running last.**

**Why:** `ViewerSession` launches ONE viewer for the whole assembly, deliberately — a runtime, a
Direct3D device against a real adapter and a hundred-megabyte map read, paid once. Everything a test
changes is therefore seen by every test after it, and NUnit's ordering across fixtures is not
something to lean on.

The owner, 2026-08-29, on where autoplay should be tested:

> *"problem with the test, if we play other tests will fail, it basically has to be the last test
> and theres no way to set that, if it was first then it wouldnt be an issue, but last requires you
> actually set everything to a set order"*

and then allowing the alternative:

> *"'running it first and then restoring state by pausing and seeking back' is fine to do actually"*

**How to apply:** restore in the test itself, not in a teardown that a failure skips, and restore to
a value the next test can name — not "roughly back". The reason to be exact here: this suite opens
at tick 1900 because the recorder is ALIVE there and dies at 2008, so "near 1900" silently breaks
every viewmodel test after it.

**When restoring is not possible, say so and drop to a lower level.** Autoplay is not tested in the
UI suite for a specific reason rather than a general one: **the viewer has no seek action a test can
drive.** The scrub bar does not support the RangeValue pattern, and `ViewerAction` has `PlayPause`
and go-to-start but nothing that reaches a tick, so the restore cannot be written at all. The wiring
is asserted on a real `MainForm` with no window instead (`LaunchOptionWiringTests`). If a seek
command is ever added — Source spells it `demo_gototick` — this becomes writable.

**Open and deliberately undecided** (B224): the owner also raised sharing setup ACROSS tests by
leaning on the deterministic run order — enter first person once, run three tests there, leave —
with his own caveat that it *"can be flaky at times and requires you to reason about the programs
state so you have to make it a finite state machine or you will never reason it"*, and then *"idk if
thats what we should do actually, just an idea"*. Do not treat that as adopted. The measurement that
would settle it — what the mode transitions actually cost against an 11-second suite — has not been
taken.

Related: [[output-level-assertion-or-it-is-not-done]],
[[read-the-trx-total-not-the-console]], [[a-negative-retry-is-a-sleep]],
[[nunit-shared-fixture-is-the-standard]].
