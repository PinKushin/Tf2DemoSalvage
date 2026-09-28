---
name: valve-parity-is-the-first-principle
description: Performance never buys a departure from Valve — matching the engine is where every measured win came from.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T03:30:26.470Z
---

Owner, mid-optimisation: *"this isnt messing up the matching valve rule is it? we gained a lot of
performance my matching valve, but weve seemed to lose part of them"* → *"ok well dont change things
that are valve parity, keep valve parity as first principal."* Recorded as D89.

**Why:** parity is the constraint the performance work happens INSIDE, never a factor traded against
speed. Every measured win on this viewer has been a move TOWARD the engine (one static mesh per
model, precaching at load, reusing buffers — all matching Valve's own arrangement).

**How to apply:**
- Before proposing a performance change, find the engine's arrangement for the same problem. If ours
  already matches it, the cost is elsewhere.
- A candidate optimisation that departs from Valve is FIRST EVIDENCE the engine's arrangement isn't
  understood yet, not a trade to evaluate.
- If the engine's arrangement genuinely IS the cost (profiled, not assumed), declare the departure
  with the measurement that forced it (D86).
- A change moving toward Valve needs no justification, even when its purpose is speed.

Related: [[name-the-trade-before-fixing-valve]], [[instrument-bugs-outnumber-decoder-bugs]],
[[parity-is-the-search-not-the-defence]].

---

## `parity-is-the-first-hypothesis` — D148, and it governs DIAGNOSIS rather than design

Owner: *"basically any time theres anything wrong, look at valve parity first."* Four for four in one
session: an invented solve, missing static props, a wrong collision mask, a loop transcribed at the
wrong scope — none was an ordinary coding mistake.

**The speed half is easy to miss:** the engine runs at 66 ticks/second with far more load than us —
when our transcription can't keep up, the likely cause is a DIFFERENT algorithm, not the same one
slowly. Ask whether one iteration of our loop covers the same thing as one iteration of the engine's.
A hook refuses a fix commit citing no engine function; `not-parity:` is the audited escape.

---

## `an-optimisation-is-not-a-skippable-departure` — Valve's optimisations earn their place

Proposing to match Valve's bone pipeline while skipping its threaded setup as "just an optimisation",
owner: *"go for the threading too, full parity thats an optimization valve did for vary good reason,
the bones are heavy, they need speed."*

**Why:** a shipping optimisation was written against a measured frame budget — it earns its place by
default, and the project's own measurements already showed exactly the cost it targets, unchecked
before proposing the skip.

**How to apply:** when tempted to file something as "merely an optimisation", find the measurement
that decides it — usually already exists. Treat a departure from an optimisation as needing the same
evidence as a departure from behaviour. Note what the optimisation actually is (a speculative
prefetch, not naive parallelism) — the name can mislead.

---

## `baking-yields-to-parity` — D143, ours is the side that changes

Owner: *"valve doesnt have baking, soo, their version isnt going to bake, and we might have to change
our baking if valve does something that is imcompatable."* When a piece of engine behaviour (e.g. a
per-view distance fade) won't fit a baked static path, change the path — do not look for the version
of the behaviour that fits the optimisation.

---

## `refactors-are-when-to-check-parity` — the check is nearly free while the shape is being decided

Owner: *"the refactors are perfect times to double check stuff like that."* Reading Valve's
arrangement for a job being extracted costs one grep at exactly the moment the new shape is decided —
and a divergence written into a freshly-extracted type is harder to spot later than one left in an
old method. Found once within minutes: an extracted method read ambient camera state off the form
where Valve's equivalent is TOLD the camera via a parameter struct — exactly what made the extracted
method untestable without a window.

**How to apply:** find the engine's equivalent, read what it's PASSED vs. what it reaches for
(parameters vs. ambient state is usually the whole difference between testable and not). Record the
comparison even when ours matches.

Related: [[conformance-test-before-implementation]], [[nothing-is-closed]].

---

## `a-transcribed-function-is-not-a-ported-subsystem` — D172, parity is claimed of a structure

Owner, midway through a large physics refactor: *"how was everything so fucked up we needed this
massive refactor anyway? i thought we had all this on parity and working but idk."* A faithfully
transcribed impact solver sat inside collision plumbing that was this project's own invention, labeled
only in code remarks — per-function claims of parity added up, for the owner, to a subsystem claim,
while measured failures (a corpse bouncing forever, buried limbs) were treated as tuning problems.

**How to apply:**
- Claim parity of a SUBSYSTEM, never a function — name which structure is still ours in the same
  sentence as the function that's the engine's.
- A transcribed centre inside invented plumbing is a partial port; say so beside the code.
- Tuning that measures worse twice means the shape is wrong — read the structure's callers before a
  third attempt.
- A decompiler's variable is not the disassembly's operand — verify against instructions.

---

## `ponytail-works-inside-parity` — the lazy rule never cuts an engine branch

Owner, switching styles mid-session: *"keep in mind ponytail does not overide, valve parity, but it
should end up being the same thing anyway."*

**How to apply:** "does this need to exist?" is answered by the ENGINE, not by our current callers —
port a branch Valve has even if nothing currently reaches it, then make the oracle reach it. Lazy
applies to OUR plumbing only (no invented abstraction around ported code). A dead computation the
engine itself discards may be left out only when PROVED dead, never on appearance.
