---
name: two-accumulators-cannot-see-order
description: Two lists filled by two different callbacks cannot measure which callback ran first; the observer has to read the observed.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:52:47.282Z
---

**A test that records two things into two lists and asserts on both looks thorough and is blind to
ordering.** Written for IVP's event loop, where the engine sets the environment's clock BEFORE
firing each event:

```csharp
manager.Add(new PhysicsEvent(1f, seen.Add));      // what the event was told
manager.DrainUntil(5d, clock.Add);                // what the clock was set to

seen.ShouldBe([1d, 2d]);
clock.ShouldBe([1d, 2d, 5d]);
```

Both assertions are exact predictions, both are true, and **swapping `setClock(absolute)` and
`next.Fire(absolute)` in the code under test leaves both true.** `seen` and `clock` are independent
accumulators; neither observes the other, so the ORDER of the two calls that fill them is not a
variable this experiment measures at all. Sabotage caught it. No strengthening of either assertion
could have — this is trap 1, wrong instrument, not a weak assertion.

**The fix is to make the observer read the observed:**

```csharp
double clock = double.NaN;
manager.Add(new PhysicsEvent(1f, _ => observed.Add(clock)));   // the event reads the clock
manager.DrainUntil(5d, now => { clock = now; ticks.Add(now); });

observed.ShouldBe([1d, 2d]);
```

Fire-first now leaves the first event looking at `NaN` and the second at `1`, so correct and broken
predict different observations — which is the whole requirement.

**Why:** ordering between two collaborators is only observable where one of them can see the
other's state. Recording each side separately measures the VALUES each was given, which is a
different variable and one that both orders satisfy.

**How to apply:** when the claim contains the word *before*, *after*, *already* or *not yet*, ask
which side has to READ the other. If the answer is "neither, they both just append", the test cannot
fail and needs restructuring, not a bigger assertion. Related:
[[instrument-bugs-outnumber-decoder-bugs]], [[most-of-a-decoder-is-untested]] and its
`a-duplicated-guard-cannot-be-tested` section.
