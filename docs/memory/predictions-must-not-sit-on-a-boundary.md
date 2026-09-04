---
name: predictions-must-not-sit-on-a-boundary
description: Exact-decimal arithmetic predicting a float measurement fails at integer boundaries.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-04T05:32:47.858Z
---

**A test prediction computed in exact decimal, measured on a float path, must not land on an
integer boundary.** Twice in two days, both times the code was right and the prediction was wrong:

- B307: window 0.2 to 0.7 at cycle 0.25 gives `0.05f / 0.5f` = 0.099999994, so 30 frames of it is
  2.9999998 and the index floors to **2**, not 3.
- B309: `frac(3.30)` is `3.3f - 3` = 0.29999995, so 30 frames of it is 8.999998 and floors to **8**,
  not 9.

Both were investigated as defects first. Neither was.

**Pick inputs whose answer lands mid-frame.** 0.1 to 0.9 at cycle 0.25 gives 5.625; time 3.35 gives
10.5. Rounding cannot reach a neighbour from there, so the prediction survives any reasonable
change in float ordering.

**The tell is a predicted value that is a round number**, especially a whole frame index with a
fraction of exactly 0 or 1. That is where to suspect the prediction before the code
([[suspect-the-input-not-the-algorithm]] is the same rule from the other side — there the input was
wrong, here the arithmetic ABOUT the input is).

Assert the fraction as well as the index when the subject is a position in an animation: it turns a
one-off boundary coincidence into a two-number prediction that cannot be satisfied by accident.
