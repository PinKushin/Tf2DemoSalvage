---
name: a-partial-thin-view-is-worse-than-none
description: "A mostly-thin view reads as \"logic here is acceptable\" and the next session extends the precedent; enforcement is the TFM, not the file."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:55:14.885Z
---

Owner, 2026-08-25: *"a true view has zero domain knowledge, nothing a presenter or model would do. it
also is one of those things that are worse when its not followed since im using AI, because it
invites later AI to not follow the convention and we get a fat view again. we also get far better
compile time protection by moving it all out."* Recorded as **D90**.

**Zero domain knowledge is the definition, not an aspiration.** A one-line delegator like
`PlayerModel(p) => PlayerProps.ModelFor(p, …)` is the view knowing a domain operation exists — brief
does not make it view code.

**A partial job is worse than none — proved twice here.** MVP (D54/D62): B188 records *"Nothing else
followed… everything written since has gone into the form because that is where its neighbours
are."* Test naming: 2,132 tests drifted to the opposite of the written standard because one early
file set the style. A 90%-thin view reads as "logic in the view is acceptable here", and the next
change extends that precedent.

**Enforcement is the TFM, not the file:** `net10.0` cannot reference WinForms — a compile error.
Moving logic to another file inside `Viewer3D` buys nothing if the project is still
`net10.0-windows`. "Move it out" means out of the PROJECT.

**How to apply:**
- No delegating wrappers left behind — the view asks a presenter it already holds.
- Callbacks the view SUPPLIES are domain services too (`LightAt`, `SunAt`, `Sample`,
  `ModelGeometry`).
- Orchestration stays even in a frame loop (`RenderFrame`'s pump); its phase order leaves.
- Test is never line count, but whether a second frontend would REIMPLEMENT anything in the file —
  owner: *"the line count isnt a actual target, making the mainform into a true thin view is, the
  line count is just a smell"* — track the member list view/not-view to zero, quote lines only as
  symptom.
- Scope is never a reason to stop short: *"i know the scope is large but this project needs to be a
  true view, no knowledge about the domain is allowed."*

Related: [[conformance-test-before-implementation]], [[output-level-assertion-or-it-is-not-done]],
[[one-place-or-it-drifts]], [[valve-parity-is-the-first-principle]].
