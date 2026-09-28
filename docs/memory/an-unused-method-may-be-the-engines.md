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
nothing called `X`, not that `X` shouldn't be called. When the method transcribes engine behaviour, an
unused one means a CALL SITE was lost — a divergence deletion would make permanent and invisible.

B382: mid-refactor the build flagged `Renormalise` and `Neighbours` as unused. `Neighbours` was
genuinely superseded. `Renormalise` was **Valve's `TimeFixup_Hermite`** (`interpolatedvar.h:1372`) —
the rewrite had just stopped calling it. One keystroke from being committed as cleanup, green suite,
zero warnings.

**Why:** this codebase's private methods largely transcribe named engine functions, which an analyzer
can't know — reachability ("does anything call it") and parity ("should something call it") share a
symptom and have opposite answers.

**How to apply:** before deleting an "unused" method, read its doc comment for a Valve name or
file:line. If present, restore the call rather than delete — often to a different home (here, the
history class, not the track). Two more losses from the same refactor surfaced the same way with NO
warning at all: the causality gate (B94) and a wake scheduler describing a structure the sampler no
longer read.

Related: [[filing-a-divergence-is-not-fixing-it]], [[the-base-is-not-the-behaviour]],
[[a-guard-you-remove-may-be-the-mechanism]],
[[the-interpolation-pair-is-found-by-changetime#the-prune-keeps-two-stale-entries]].

---

## `a-calling-shape-is-not-a-purpose` — name a function by what it WRITES

Two `vphysics.dll` functions were labelled "the island solve" on the strength of their SHAPE (a
vtable dispatch taking a float time budget) — every observation true, label wrong. They're a
**contact-pair re-check scheduler**. IVP dispatches EVERY scheduled thing through the same vtable
slot with a time, since it's event-driven — reading "vtable + time argument" as evidence of a solver
is reading house style as fingerprint. The real integrator was three plain calls deep, behind none
of it.

**Happened again with CONSTANTS instead of a call shape.** `FUN_180019cc0` was named gravity because
its unit-conversion constants (0.0254 inches-to-metres, 0x80000000 IEEE sign bit) matched
`SetGravity` byte-for-byte — but those constants appear in every function touching a vector across
the Source-to-IVP boundary. The function is actually `IPhysicsMotionController`'s per-object step,
settled by its four branches matching the published `simresult_e` enum exactly.

**How to apply:** name a function from the fields it changes, never how it's invoked or which
constants it borrows. Ask what OTHER function would satisfy the same description — "hundreds" means
you've described the convention, not the function. A published enum or written field settles it in
lines where shape-matching took months. Related: [[instrument-bugs-outnumber-decoder-bugs]],
[[a-flag-with-no-field-is-set-by-the-loop]], [[nothing-is-closed]].
