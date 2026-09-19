---
name: take-the-desktop-lock-dont-defer
description: "Run desktop-bound work (UI gate phase 2, viewer shots) through run-exclusive.ps1 instead of leaving it for the owner; the session cannot tell if he is at the machine."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-18T15:37:28.858Z
---

Run UI-suite and viewer work yourself through `run-exclusive.ps1`; do not stop and leave it "for when the desktop is free".

**Why:** 2026-09-18, after I ended a turn with "phase 2 not run — takes the desktop", the owner said: *"I'm not even home so the desktop is yours, can you not tell if I'm using a remote or local?"* The session gets no reliable signal for local vs remote. The lock already arbitrates between people and agents, and it waits rather than fights.

**How to apply:** when the gate's phase 2 or a `--shot`/`--measure` is the next step, run it under `run-exclusive.ps1` in the same turn. See [[ui-tests-run-every-time]] and [[take-your-own-screenshot]].
