---
name: gate-once-per-batch
description: Full phase 1 runs once per batch right before merge; between review rounds only the touched test projects. Merges batch into one gate. Two agents only pay off when a UI run can start beside phase 1.
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-18T23:50:02.593Z
---

Owner, 2026-10-18: *"im just really tired of gate 1 stepping on everything lol"*, then *"yeah apply both, if we cant run ui concerently to gate 1 we cant run more than one subagent, theres no point"*.

**Why:** every phase 1 holds the lock and every UI run waits behind it ($SharedBesideExclusive = 0 since Rendering.Tests peaked at 18 GB). Agents re-running phase 1 per review round, plus one gate per merge, kept the desktop queued for hours.

**How to apply:**
- Agents run only the touched test projects between rounds, and full phase 1 + phase 2 once, at the end of a batch, before reporting merge-ready.
- The lead batches finished branches into one merge gate (docs-only branches ride along with the next code merge).
- While UI cannot run beside phase 1, a second agent buys nothing: fix that first (Rendering.Tests under ~3 GB, then $SharedBesideExclusive = 1), and run one implementer until then.
Related: [[gate-once-per-merge-not-per-commit]], [[parallel-agents-lock-the-gate-too]], [[one-subagent-and-prefer-cheap-models]].
