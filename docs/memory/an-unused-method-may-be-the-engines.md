---
name: an-unused-method-may-be-the-engines
description: "An analyzer's \"remove the unused private method\" can mean a call site was lost, not that the code is dead — ask what engine function it transcribes before deleting it."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T21:54:29.262Z
---

**`error S1144: Remove the unused private method 'X'` is a question, not an instruction.** It says
nothing called `X`; it does not say `X` should not be called. When the method transcribes engine
behaviour, an unused one means a CALL SITE was lost — which is a divergence, and deleting the method
makes it permanent and invisible.

Measured, B382: mid-refactor the build reported

```
error S1144: Remove the unused private method 'Renormalise'.
error S1144: Remove the unused private method 'Neighbours'.
```

`Neighbours` was genuinely superseded. `Renormalise` was **Valve's `TimeFixup_Hermite`**
(`interpolatedvar.h:1372`) — the respacing that makes a hermite spline usable on unevenly spaced packets
— and the rewrite had simply stopped calling it. One keystroke from being committed as a cleanup, with a
green suite and a build at zero warnings.

**Why:** this codebase's private methods are largely transcriptions of named engine functions, and an
analyzer cannot know that. The reachability question ("does anything call it") and the parity question
("should something call it") have the same symptom and opposite answers.

**How to apply:** before deleting anything the analyzer calls unused, read its doc comment for a Valve
name or a `file:line`. If it has one, the fix is to restore the call, not to delete the method — and the
right home for it is usually where the engine keeps it, which in that case was on the history class
rather than on the track. Two more losses from the same refactor turned up the same way, and neither
raised a warning at all: the causality gate (B94) and the wake scheduler still describing a structure the
sampler no longer read. **A refactor's real damage is the calls it stops making, and only one of those
three was visible to a tool.**

Related: [[filing-a-divergence-is-not-fixing-it]], [[the-base-is-not-the-behaviour]],
[[a-guard-you-remove-may-be-the-mechanism]],
[[the-interpolation-pair-is-found-by-changetime#the-prune-keeps-two-stale-entries]].

---

## `a-calling-shape-is-not-a-purpose` — name a function by what it WRITES

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

**It happened a second time, with CONSTANTS instead of a call shape, and the second one was mine.**
`FUN_180019cc0` was written up as the best candidate for gravity because its unit-conversion
fingerprint was "byte-for-byte `SetGravity`'s" — the same scale and the same sign mask. Those
constants dump as **0.0254** (inches to metres) and **0x80000000** (the IEEE sign bit), and they
appear in every function that moves a vector across the Source-to-IVP boundary. The function is
actually `IPhysicsMotionController`'s per-object step, settled by its four branches matching the
published `simresult_e` enum exactly.

**A shared constant is the same trap as a shared calling convention.** "Uses the engine's unit
conversion" is true of everything that touches a vector, so it narrows nothing — and it feels like
strong evidence precisely because it is specific and checkable.

**How to apply:** name a function from the fields it changes, never from how it is invoked or from
which constants it borrows. Before writing a label into a document, ask what OTHER function in the
binary would satisfy the same description — if the honest answer is "hundreds", the description is
of the convention rather than of the function. **The way out both times was a published enum or a
written field**: `simresult_e` named this one in four lines, where months of shape-matching had
not. And when a shape-based label is later falsified, keep it struck through with what
killed it: it is the second time a wrong conclusion here came from a pattern that was genuinely
present and genuinely uninformative. Related: [[instrument-bugs-outnumber-decoder-bugs]],
[[a-flag-with-no-field-is-set-by-the-loop]], [[nothing-is-closed]].
