---
name: a-calling-shape-is-not-a-purpose
description: In a binary with one generic dispatch convention, every function looks like the one you are hunting; a vtable call with a time argument identified nothing.
metadata:
  type: feedback
---

**Two functions in `vphysics.dll` were labelled "the island solve" on the strength of their SHAPE**
— a vtable dispatch, `(**(code **)(*ev + 8))(ev, this, dt)`, taking a float time budget, reached
from inside the physics step. Every one of those observations was true, and the label was wrong.
`FUN_18009a690` / `FUN_18009a4f0` are a **contact-pair re-check scheduler**: the chain below them
computes the relative velocity between two cores and re-queues when that pair should next be
broad-phase checked.

**Why the shape proved nothing: it is the convention the whole engine is built on.** IVP dispatches
EVERY scheduled thing through slot 1 of its own vtable with a time, because it is an event-driven
simulation and the event queue is everywhere. Reading "vtable + time argument" as evidence of a
solver is reading the house style as a fingerprint. The real integrator, when it turned up, was
three plain calls deep and behind none of it.

**Why:** in a stripped binary the only things visible are shapes — call signatures, argument counts,
offsets touched. That makes it tempting to identify a function by how it is CALLED, which is exactly
the property a generic convention destroys. What identifies a function is what it WRITES: the
integrator was found by looking for writes to the core's position and orientation fields, and it was
unambiguous the moment those were seen.

**How to apply:** name a function from the fields it changes, never from how it is invoked. Before
writing a label into a document, ask what OTHER function in the binary would satisfy the same
description — if the honest answer is "hundreds", the description is of the convention rather than
of the function. And when a shape-based label is later falsified, keep it struck through with what
killed it: it is the second time a wrong conclusion here came from a pattern that was genuinely
present and genuinely uninformative. Related: [[an-empty-search-needs-a-control]],
[[a-flag-with-no-field-is-set-by-the-loop]], [[absent-from-the-sdk-is-not-unreadable]].
