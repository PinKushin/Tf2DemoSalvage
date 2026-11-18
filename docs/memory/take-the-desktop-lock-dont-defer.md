---
name: take-the-desktop-lock-dont-defer
description: "Run desktop-bound work (UI gate phase 2, viewer shots) through run-exclusive.ps1 instead of leaving it for the owner; the session cannot tell if he is at the machine."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-18T15:37:28.858Z
---

Run UI-suite and viewer work yourself through `run-exclusive.ps1`; do not stop and leave it "for when
the desktop is free."

**Why:** owner, after a turn ended with "phase 2 not run — takes the desktop": *"I'm not even home so
the desktop is yours, can you not tell if I'm using a remote or local?"* The session has no reliable
local-vs-remote signal. The lock already arbitrates between people and agents, and it waits rather
than fights.

**How to apply:** when the gate's phase 2 or a `--shot`/`--measure` is the next step, run it under
`run-exclusive.ps1` in the same turn. See [[ui-tests-run-every-time]], [[take-your-own-screenshot]].

**Live TF2 too, and asking first counts as deferring** (2026-11-17): B768's live measurement was parked all day and
then put to the owner as "tell me when it's fine to launch TF2". Owner: *"you actually had the desktop most of the day
so it would have been fine"*. A live-TF2 measurement is desktop work like any other: take the lock and run it.
