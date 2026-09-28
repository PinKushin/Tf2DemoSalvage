---
name: ask-whether-it-still-follows-the-pattern
description: "\"Is this still MVP\" found two defects that tests, logs and an end-to-end measurement had all missed — including the real cause of the bug just fixed."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 71bbc8e9-0f7a-489c-a987-3e0867aae1fa
  modified: 2026-09-10T22:55:15.660Z
---

**After a fix lands, ask what it did to the ARCHITECTURE, not just whether it works.**

2026-08-29: a B223 fix was sabotage-verified, measured end to end, merged, pushed. Owner then asked:
*"this is still following MVP right"*. Checking honestly found two more defects, the first being the
actual cause of the bug just "fixed":

1. **`TransportBar.SetDemoLength` ended with `Playing = false`** — the View deciding business state.
   D55: *"If a Form method needs an `if` statement about business state, that's the tell it's doing
   the Presenter's job."* The merged fix moved the CALL so nothing could run between it and `Play()`
   — closed the hole, left the trapdoor. Deleting the side effect removes it entirely.
2. **A stale clock**, reachable only once the side effect was gone. `DemoSystems.Open` nulled every
   other source on the failure path, leaving the presenter holding the previous demo's clock.

**Why the others missed it:** a test asks "does this behave"; a log "what happened"; a measurement
"is the number right" — all satisfied. "Does the View decide anything" asks who OWNS a
responsibility, which works fine until a second caller appears.

**How to apply:**
- Ask it after the fix is green, not instead of getting it green.
- Check the layer's own written rule (D55's tell is a sentence you can hold a method against).
- A guard around a hazard is not removal — ask what makes the wrong order possible and delete that.
- When the real object changes, its fake (`FakePlaybackView`) is the second place that must change.
- Watch for a test whose precondition already equals its assertion — how the stale clock hid.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[one-place-or-it-drifts]],
[[parity-is-the-search-not-the-defence]], [[most-of-a-decoder-is-untested]],
[[output-level-assertion-or-it-is-not-done]], [[boundaries-find-what-tests-cannot]].
