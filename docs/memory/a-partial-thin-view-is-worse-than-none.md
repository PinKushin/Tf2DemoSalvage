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

## It regressed unguarded, and line count is NOT the measure (2026-10-12)

- Found via Viewer3D's mutation score (8.47%): the view project references Core, Content, Scene, Audio
  directly, so every "put it in `MainForm`" compiled. Five implementer reports said "MainForm call site is
  untested wiring" and each was merged. Owner: *"fuck i thought i had a hook for this already, but we need one"*.
- I measured `MainForm.cs` lines (4,266 after the D90 thinning, 10,162 now) and wrote a no-growth hook. Owner:
  *"the line number should groww though, thin view is about domain logic, not lines of code"*; *"when we add
  options to the winforms skeleton, its obviously going to groww"*. Size proves nothing either way.
- **Measure = domain reach**: types/members the view assembly uses from Core/Content/Scene/Audio. Agreed plan:
  an architecture test with a committed list that may only shrink; a hook that denies ADDING to that list;
  end state is the list empty and the four project references removed, so the compiler enforces D90.
- **How to apply:** every implementer brief says: no new domain type or member in `Viewer3D`; behaviour goes in
  Presentation/Scene/Render with tests; the view gets one call. A report that says "untested wiring in
  MainForm" goes back, not to merge.
- **Owner, 2026-10-13, asked whether the device's types may stay the view's vocabulary: *"1 true thin view"*** (relayed
  by the coordinating agent; option 1 was the presenter driving the device through its own interface and the Scene
  reference leaving). So no Scene, Core or Content type is "just vocabulary" in `Viewer3D`: a type the view only
  passes through is still reach, and it goes behind an interface (D214).
- **End state reached 2026-10-13 (B540 closed): reach 0, Core/Content/Scene/Audio references out of the csproj.** The
  compiler does NOT enforce it — domain assemblies still arrive through Presentation and Render, and `Device3D`
  implements Scene interfaces, so they cannot be shut out. `ThinViewConformanceTests` (3 tests) holds it: IL reach
  empty, no domain ProjectReference, no domain namespace in a code line. The IL instrument cannot see an inlined
  `const`; the source test can.
- **Reach 0 was not thin (slice 9, same day).** The owner: *"are you sure thin view is fully closed, and wont ever be
  made fat again?"* It was not: the idle loop, frame steps, capture and measure runs, key dispatch and render
  switches were all still in `MainForm`, written over Presentation objects held as fields, which reach does not
  count. They are `ViewerSession`; the window is `IViewerWindow` + `IIdleHost`. A fourth test holds the mechanism:
  no view type has a field of a class or interface from Presentation, Render or the domain except the session.
  **How to apply:** when a measure reads zero, ask what it does not measure before calling the thing done; and a
  new view field that is a presenter, a device or any object from below goes on the session instead. The field
  rule is sound, not complete: a decision in locals between two session calls still needs a reviewer.

Related: [[conformance-test-before-implementation]], [[output-level-assertion-or-it-is-not-done]],
[[one-place-or-it-drifts]], [[valve-parity-is-the-first-principle]].
