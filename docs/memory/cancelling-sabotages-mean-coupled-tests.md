---
name: cancelling-sabotages-mean-coupled-tests
description: Two sabotages at once left a test green that either alone would have reddened; the fault was a shared input producing a tie, and the fix is the input, not another test.
metadata:
  type: feedback
---

**Sabotage ONE thing at a time, and when two cancel, fix the test's input rather than adding a
third test.**

On 2026-08-20, verifying `BspCubemaps.Closest` by manipulation, two sabotages went in together:
the height term zeroed (`dz * dz * 0`) and the comparison loosened (`<` to `<=`). Only one test
went red. The axis test — the one written specifically to catch a search blind on Z — stayed
**green against a search that was blind on Z**.

**The cause was the test's input, not its assertion.** Its two placements shared X and Y exactly,
at `(0,0,0)` and `(0,0,500)`, measured from `(0,0,480)`. Isolating the height term that cleanly
looks like good practice and is the trap: with Z dropped both distances collapse to **zero**, so
the answer stops being decided by distance at all and is decided by how a tie resolves — which
was the other sabotage. The two tests were coupled through one input, and the second sabotage
supplied exactly the tie-break the first one needed.

The owner asked the right question — *"if two sabotages cancel should we have a third test to
catch that?"* — and the answer is no. A third test would cover this pair and not the next one;
the combinations are unbounded, and Stryker only generates first-order mutants anyway.

**Fix the condition.** Offsetting the placements 30 units on X removes the tie: with Z counted the
near one is 1,300 units² away against 230,400, and with Z dropped it is 900 against 0 — opposite
answers, no tie, no dependence on the comparison operator. Re-running the same double sabotage
then reddened **both** tests.

**How to apply:**

- **One sabotage at a time.** Two at once can cancel, and a green suite then reads as proof the
  code is right when it is proof of nothing. This is the manipulation step's own failure mode.
- **A test whose verdict depends on another behaviour being correct is not measuring what it
  names.** Ask of every test: with the thing I am testing broken, does the observation differ —
  or does it merely become *undetermined*, and get decided by something else?
- **A tie is the specific shape to watch for.** Breaking a comparison, a distance, a sort key or a
  score usually makes two candidates EQUAL rather than misordered, and equality is then resolved
  by a tie-break the test never meant to exercise. Perturb the other axes so the broken version
  gets a definite wrong answer.
- This is [[a-test-can-outlive-its-design]] seen from the other end, and CLAUDE.md's case 2 —
  *wrong condition: an input for which correct and broken predict the same observation. Fix the
  input, not the assertion.* Related: [[instrument-bugs-outnumber-decoder-bugs]].
