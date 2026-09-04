---
name: valve-parity-is-the-first-principle
description: Performance never buys a departure from Valve — matching the engine is where every measured win came from.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-25T05:24:00.762Z
---

The owner, 2026-08-25, mid-optimisation:

> "this isnt messing up the matching valve rule is it? we gained a lot of performance my matching
> valve, but weve seemed to lose part of them"

> "ok well dont change things that are valve parity, keep valve parity as first principal"

Recorded as **D89** in `docs/DECISIONS.md`.

**Why:** D82 bounds departures and D86 requires them declared where made, but neither says what
happens when a departure would be *faster*. This does: it doesn't happen. Parity is the constraint
the performance work happens inside, not one factor weighed against speed.

And the owner's observation is the empirical case for it — **every measured win on this viewer has
been a move toward the engine**:

| Change | Result | Valve's shape |
|---|---|---|
| one static mesh per model (B163) | 193–231 ms × 25 → gone | `CreateStaticMesh` |
| precache models at load (B163) | 385–425 ms in-frame → 515 ms once | `IsPrecacheAllowed()` |
| precache sounds at load | 27–91 ms every few seconds → 2,261 ms once | `Assert( "PrecacheSound: too late" )` |
| reuse the skinning buffer | ~20,000 arrays a frame → none | `m_CachedBoneData.SetSize` on count change |

An assistant proposal to keep the packed vertex buffer and append to it was overruled during B163
with *"so we switch to valves, which is what we should have been using in the first place, becasue
valves imp is blazingly fast"*. Same argument, and it was right then too.

**How to apply:**

- Before proposing a performance change, **find the engine's arrangement for the same problem**. If
  ours already matches it, the cost is elsewhere and the change is the wrong one.
- A candidate optimisation that departs from Valve is **first evidence the engine's arrangement is
  not understood yet**, not a trade to evaluate. Ask "what does Valve do here, and why is it fast".
- If the engine's arrangement genuinely IS the cost — profiled, not assumed — D86 applies: declare
  the departure where it is made, with the measurement that forced it.
- **A change moving toward Valve needs no such justification**, even when its purpose is speed.
  Parity restorations that happen to be faster are the ordinary case, not a lucky one.

Related: [[an-optimisation-is-not-a-skippable-departure]], [[name-the-trade-before-fixing-valve]],
[[instrument-bugs-outnumber-decoder-bugs]], [[parity-is-the-search-not-the-defence]].
