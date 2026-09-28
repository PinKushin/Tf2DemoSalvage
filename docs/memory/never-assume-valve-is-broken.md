---
name: never-assume-valve-is-broken
description: "When our output looks worse than TF2's, the fault is ours; never explain a gap by \"the engine does it badly too\""
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 168f3a6d-d771-45c7-958f-2de3d0ee4765
  modified: 2026-09-22T02:42:03.853Z
---

**Never explain a shortfall by assuming Valve's engine has the same flaw.** After concluding that
blood decals on moving players "miss in TF2 too", owner: *"Never assume valve does something weird
like not drawing blood decals correctly, it's probably going to be wrong."* Correct: `CL_QueueEvent`
(`engine.dll` `0x1801f9bc0`) delays every temp entity by `GetClientInterpAmount()` so effects fire on
the pose they were sent for; we fired on arrival.

**Why:** concluding the engine is broken ends the search — feels like parity, is really an unread
branch.

**How to apply:** when our result looks worse than TF2's, keep reading for the mechanism we're
missing (timing/delay terms are the usual culprit). Any "the engine also does X" claim needs a
citation or a check in the running game. Related: [[valve-parity-is-the-first-principle]],
[[a-filed-design-choice-may-not-be-one]].
