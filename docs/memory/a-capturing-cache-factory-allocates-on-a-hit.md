---
name: a-capturing-cache-factory-allocates-on-a-hit
description: A lambda that captures a local, passed to a cache's GetValue, makes its closure on every call, even a hit; and IvpTestSurface boxes are in IVP's frame.
type: feedback
---

**A cache lookup whose factory lambda captures a local allocates on every call, hit or miss.** C# lifts the captured
local into a closure object at the scope's entry, not at the lambda's use. `IvpLedgeSide.FromLedge` did
`if (!table.TryGetValue(k, out v)) v = table.GetValue(k, _ => Build(ledge))`, and that one closure cost 281 MB per 20 s on
f12 (finding 83). Move the miss into a method of its own, so the closure only exists there.

**Why:** an allocation profile names it `<>c__DisplayClassNN_0`, attributed to the caller's caller, so it reads as
"something in IVP" rather than one line.

**How to apply:** when a trace shows a `DisplayClass` under a hot path, look for a capturing lambda anywhere in the
method, including one that only runs on a rare branch.

**Also learned in that work:** `IvpTestSurface.Boxes` takes centres and half extents in **IVP's frame, whose second axis
is Source's up**. A "floor" written as `(20, 20, 1)` is a 40-metre-tall wall that encloses the corpse, which then falls
through it with zero impacts. Write a slab as `(20, 0.5, 20)`.
