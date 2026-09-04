---
name: it-ran-and-it-mattered-are-two-claims
description: A counter proving a stage executed cannot say it changed anything; measure the effect.
metadata:
  type: project
---

**A counter that proves a stage RAN cannot say it changed anything, and the difference is usually
the whole question.** Instrument the effect, not the execution.

B311, 2026-09-04. `IkLocks.Applied` reported 88 sequence locks running on a real demo — good enough
to prove the wiring, and useless for the thing anyone cares about. A lock whose remembered position
already equals where the sequence left the foot solves to the same place: **the bracket runs, the
pose is unchanged, and on screen that is indistinguishable from the lock never running at all.**

Adding the distance settled it: `88 moved, furthest 3.81 units`, on an 83-unit-tall player — about a
foot's width, which is the slide being removed.

**This is how a "needs a person looking" question becomes answerable.** A screenshot cannot show
that a foot stopped sliding without a before and an after of the same motion. A bone position is
deterministic, so the correction has a magnitude, and a magnitude can be asserted.

**Report the COUNT and the MAXIMUM, never one alone.** A hundred corrections of a thousandth of a
unit is arithmetic running, not a foot being held — and a threshold (0.01 here) is what separates a
correction from float noise.

**Carry both out of the loop that did the work** ([[a-defect-that-survives-its-cause-is-in-the-instrument]]):
the pre-solve value is the one the solve was handed, the post-solve one comes from re-reading what
the solve wrote. A second derivation of either is free to be wrong and looks authoritative.
