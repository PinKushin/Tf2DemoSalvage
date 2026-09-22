---
name: never-assume-valve-is-broken
description: "When our output looks worse than TF2's, the fault is ours; never explain a gap by \"the engine does it badly too\""
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 168f3a6d-d771-45c7-958f-2de3d0ee4765
  modified: 2026-09-22T02:42:03.853Z
---

**Never explain a shortfall by assuming Valve's engine has the same flaw.** Owner, 2026-09-21, after I concluded that
most blood decals on moving players "miss in TF2 too": *"Never assume valve does something weird like not drawing blood
decals correctly, it's probably going to be wrong"*. He was right: `CL_QueueEvent` (engine.dll `0x1801f9bc0`) delays
every temp entity by `GetClientInterpAmount()`, so effects fire on the pose they were sent for. We fired them on arrival.

**Why:** a conclusion that the engine is broken ends the search. It feels like parity, but it's really an unread branch.

**How to apply:** when our result looks worse than what a player sees in TF2, keep reading the engine for the mechanism
we're missing. Timing and delay terms are the usual culprits. Any claim that "the engine also does X" needs a
citation, or else a check in the running game. Related: [[valve-parity-is-the-first-principle]],
[[a-filed-design-choice-may-not-be-one]].
