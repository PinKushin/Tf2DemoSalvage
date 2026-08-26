---
name: a-superseded-type-keeps-its-tests
description: When a replacement takes over the call sites, the old type's tests keep passing and answer "is this tested?" with yes.
metadata:
  type: project
---

**When a type is superseded, the call sites move and the tests do not.** The old type keeps a green
suite describing behaviour nothing executes, and the new type inherits the responsibility with no
coverage at all. Nothing fails at the moment the tests stop meaning anything.

**Measured, 2026-08-26 (B206).** `FreeLookState` had `Drag`, `PlaceAt`, `Unplace`, `Fly` and
**eleven tests**, and no production caller anywhere — the only outside reference was a stale name in
a doc comment. `FreeCameraController` had superseded it (D66 created the first, D90/D91 replaced it),
took over every call site, and had **zero** tests. Meanwhile the drag the viewer actually performed
was written longhand inside `MainForm.OnViewportMouseMove` with its own copy of `DegreesPerPixel`.

So the position was exactly inverted: **the mouse look that ran was unwatched, and the one with
eleven tests never executed.**

**Why this is worse than ordinary dead code**, which is the point of the entry. Dead code is waste.
Dead code *with a passing test suite* is a **false negative**: the question "is the drag tested?"
answers yes, correctly, about the wrong object. It is [[measure-the-output-not-the-capability]] one
level up — the instrument reports a capability that exists and is not connected to anything.

**How to apply:**

- **When you supersede a type, grep the old one for production callers before leaving it.** Zero
  callers plus a test file is the signature. This is cheap and nobody does it, because the suite is
  green and the new work is finished.
- **Migrate coverage selectively, and let the arithmetic check you.** Of eleven tests, six `Fly`
  cases were already covered on the live path by `CameraFlightTests` and `FreeFlightPathTests`, so
  porting them would have been duplication; four `Drag` cases and one clamp were not covered
  anywhere and were ported. Net −11 +5, and `build/gate.sh`'s exact floor refused the drop until the
  reasoning was written beside the new number.
- **A floor drop is the moment to justify a deletion, not a step to get past.** The gate's message
  says "if removed, lower the floor" — the comment recording *which* tests went and *why nothing was
  lost* is the artefact that makes the deletion reviewable later.

Related: [[unreachable-can-be-proved-not-just-observed]] (prove it dead, do not assume),
[[one-place-or-it-drifts]] (the duplicate constant that came with it), and B196, where two shipped
features were only ever assigned `null` and the compiler could not see it either.
