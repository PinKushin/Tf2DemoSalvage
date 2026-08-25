---
name: a-partial-thin-view-is-worse-than-none
description: A mostly-thin view reads as "logic here is acceptable" and the next session extends the precedent; enforcement is the TFM, not the file.
metadata:
  type: feedback
---

The owner, 2026-08-25:

> "a true view has zero domain knowledge, nothing a presenter or model would do. it also is one of
> those things that are worse when its not followed since im using AI, because it invites later AI
> to not follow the convention and we get a fat view again. we also get far better compile time
> protection by moving it all out."

Recorded under **D90**. All three claims hold.

**Zero domain knowledge is the definition, not an aspiration.** A passive view renders what it is
told and forwards input; it cannot answer a question about the domain. A one-line delegator like
`PlayerModel(p) => PlayerProps.ModelFor(p, …)` is the view knowing a domain operation exists —
being short makes it a concise violation, not view code.

**Why a partial job is worse than none, and this repo proved it twice.** The strongest signal for
"how do I write this" is "what do the neighbours do":

- **MVP.** D54 chose it, D62 built one presenter, B188 records the result — *"Nothing else
  followed… everything written since has gone into the form because that is where its neighbours
  are."*
- **Test naming.** 2,132 tests drifted to the exact opposite of the written standard because "one
  early file set the style, every later file matched its neighbours."

A ninety-percent-thin view does not read as nearly finished. It reads as **"logic in the view is
acceptable here"**, and the next change extends the precedent rather than the rule.

**Enforcement is the TFM, not the file.** `net10.0` cannot reference WinForms — the compiler
refuses, which is what D54 meant by a boundary that is a compile error. Moving logic to another FILE
inside `Viewer3D` buys nothing, because the project is still `net10.0-windows`. **"Move it out"
means out of the PROJECT.** Only a rule the compiler enforces survives the next session.

**How to apply:**

- No delegating wrappers left behind. If the view needs an answer, it asks a presenter it already
  holds.
- Callbacks the view SUPPLIES are domain services too — `LightAt`, `SunAt`, `Sample`,
  `ModelGeometry` were all handed to the scene by the form.
- Orchestration is not view even when it lives in a frame loop: `RenderFrame`'s pump stays, its
  phase order leaves.
- The test is never the line count. It is whether a second frontend would have to REIMPLEMENT
  anything in the file.

Related: [[decide-home-and-parity-before-writing]], [[three-test-levels-and-the-third-is-missing]],
[[one-place-or-it-drifts]], [[valve-parity-is-the-first-principle]].
