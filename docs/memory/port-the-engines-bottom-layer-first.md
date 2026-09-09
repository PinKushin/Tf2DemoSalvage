---
name: port-the-engines-bottom-layer-first
description: Port the engine's smallest named object before anything that consumes it — a top-down port retrofits every later fact into the wrong object, and its tests cannot see the bottom layer at all.
metadata:
  type: feedback
---

**The owner, on why B382 became a large refactor instead of a small fix:**

> *"these problems happened because we started top down and not bottom up i think"*

**Why:** the engine builds interpolation bottom-up. `CInterpolatedVarArrayBase` is a dumb list of entries —
a changetime and `m_nMaxCount` floats — that knows nothing about what the floats mean. `AddVar` registers
one per networked member (`c_baseentity.cpp:875`); `OnLatchInterpolatedVariables` appends to each whose
latch group fired (`:2814`); only above all of that does anything ask what pose to draw.

This project started at the top: `ScenePropTrack.At` answered *"what pose should be drawn"* over one list
of whole poses. Every engine fact learned afterwards had to be retrofitted into that one object — one
history per variable became one list with two search KEYS, the second latch clock became a side-table,
`AddToHead` being unconditional became a collapse plus `_heldUntil` to reconstruct the hold, the
arrived-only history became a guard inside `At`, and `TimeFixup_Hermite` became a private method whose call
site a later refactor dropped without a single warning. Six facts about small objects, each expressed as a
property of a large one. The result was neither ours nor Valve's.

**And it infected the tests, which is the part that is easy to miss.** Everything written for this area
asserted on the drawn pose. So when the fix's own conformance test needed to detect a missing HISTORY
ENTRY it could not: while a value is held the bracketing pair is degenerate, and the drawn pose is right
whatever the history contains. **Three sweeps in a row were written and none could fail for the fault it
named.** What worked was one exact value at the single tick where the bottom layer reaches the top.

**How to apply:**

- Port the engine's smallest NAMED object first, with its own tests, before the thing that consumes it.
- When a divergence is filed, ask which LAYER it belongs to before writing anything. A reset belongs on
  the history; "assigned on receipt" belongs to the member, not to the sampler.
- A top-level assertion is necessary and not sufficient — see
  [[output-level-assertion-or-it-is-not-done]], which points the other way and is equally true.
- The tell that a port went top-down: a fact about the engine can only be stated here as an extra KEY, an
  extra side-table, or a guard inside the consumer.

Related: [[an-unused-method-may-be-the-engines]], [[the-prune-keeps-two-stale-entries]],
[[parity-is-the-search-not-the-defence]], [[a-player-is-not-a-prop-track]],
[[filing-a-divergence-is-not-fixing-it]].
