---
name: ask-whether-it-still-follows-the-pattern
description: "Is this still MVP" found two defects that tests, logs and an end-to-end measurement had all missed — including the real cause of the bug just fixed.
metadata:
  type: feedback
---

**After a fix lands, ask what it did to the ARCHITECTURE, not only whether it works.** That is a
different instrument from a test, and it reaches things tests cannot.

**Why:** on 2026-08-29 a fix for B223 was written, verified by sabotage, measured end to end on the
viewer, merged and pushed. Every check passed. The owner then asked:

> *"this is still following MVP right"*

Checking honestly rather than answering found **two more defects**, and the first was the actual
cause of the bug that had just been "fixed":

1. **`TransportBar.SetDemoLength` ended with `Playing = false`** — the View deciding business state.
   D55's tell is exact: *"If a Form method needs an `if` statement about business state, that's the
   tell it's doing the Presenter's job."* The merged fix had moved the CALL so nothing could run
   between it and `Play()`. That closed the hole and left the trapdoor. Deleting the side effect
   removes it: the method can now be called by anyone, in any order, and cannot stop playback.
2. **A stale clock**, reachable only once the side effect was gone. `DemoSystems.Open` nulled every
   other source on the failure path and left the presenter holding the previous demo's clock.

**How the pattern question got there and the others could not.** A test asks "does this behave"; a
log asks "what happened"; a measurement asks "is the number right". All three were satisfied. "Does
the View decide anything" asks who OWNS a responsibility, and a misplaced responsibility works
perfectly right up until a second caller appears.

**How to apply:**

- Ask it after the fix is green, not instead of getting it green.
- Check the layer's own written rule rather than a general sense of the pattern. D55's tell is a
  sentence you can hold a method against; "feels like the View is doing too much" is not.
- **A guard around a hazard is not the removal of one.** If the fix was "call these in the right
  order", ask what makes the wrong order possible and whether that can be deleted instead.
- **When the real object changes, its fake is the second place that must change.** `FakePlaybackView`
  had been taught to clear `Playing` to mirror the control; keeping that would have modelled a
  control that no longer exists.
- Watch for a test whose precondition already equals its assertion — that is how the stale clock hid,
  in a file whose own comments warn about exactly that shape somewhere else.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[one-place-or-it-drifts]],
[[half-a-mechanism-is-not-parity]], [[an-environment-only-setting-is-untested]],
[[three-test-levels-and-the-third-is-missing]], [[boundaries-find-what-tests-cannot]].
