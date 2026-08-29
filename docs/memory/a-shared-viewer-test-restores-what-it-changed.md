---
name: a-shared-viewer-test-restores-what-it-changed
description: The UI suite shares one viewer, so a test that mutates it must put the state back; depending on running last is not available. The owner allows mutate-then-restore.
metadata:
  type: feedback
---

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

Related: [[ui-tests-run-every-time]], [[three-test-levels-and-the-third-is-missing]],
[[slow-ui-tests-measure-the-app]], [[a-negative-retry-is-a-sleep]],
[[nunit-shared-fixture-is-the-standard]].
