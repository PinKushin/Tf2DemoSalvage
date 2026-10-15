---
name: a-lock-holder-must-time-itself-out
description: Any script run under run-exclusive must end itself on a deadline; a hung holder blocks every agent silently
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-15T16:13:23.838Z
---

2026-10-15: the lead's memory-measuring script (a `while (-not $p.HasExited)` poll) never saw its viewer exit and held the machine lock 08:14-10:58 with nothing running, blocking every agent's gate for ~3 h. The lead didn't notice; the owner did, from silence ("I just noticed I had not gotten an update for a while").

**How to apply:** a script under run-exclusive waits with `WaitForExit(ms)` plus a deadline and kills its child on timeout; capture stderr so an instant failure is visible. When a background run is quiet for longer than expected, check the lock holder (`run-exclusive` prints it) before waiting more. Related: [[parallel-agents-lock-the-gate-too]], [[my-own-processes-are-mine]].
