---
name: a-fraction-of-zero-is-an-oracle
description: To test a drawn curve without a model of the curve, assert at the points where the interpolation fraction is zero — the sample returns itself whatever the tangents are.
metadata:
  type: feedback
---

**A duration cannot test a spline.** Three metrics in a row failed to settle B370 because they compared a
drawn motion against a constant-speed ramp, and a hermite is not a ramp: it eases out of a held position
whenever the third sample equals the second, so its time between two heights exceeds a straight line's
**with nothing wrong**. Worse, the ease-in is indistinguishable from the reported symptom — a door that
"opens late" and a door easing off a rest look identical in a duration.

**The assertion that works needs no model of the curve at all.** When the drawn target lands ON a history
entry's changetime, `GetInterpolationInfo` computes
`frac = (targettime - older_change_time) / (newer_change_time - older_change_time) = 0`
(`interpolatedvar.h:845`), and `Lerp_Hermite` at a fraction of zero returns `p1` — whatever its tangents,
whatever the respacing did, whatever the sample either side holds. So:

> **The drawn value at `changetime + interpolationDelay` must equal that entry's own value, exactly, for
> every live entry.**

No speed, no easing, no curve shape enters it. Measured across 7,068 entries on one demo it found 3–5% of
them wrong, the worst by 111 units — a whole door travel — where the duration metrics had reported the
doors as merely "80% of correct" and had also reported them, before their own bugs were fixed, as *faster*
than stated.

**How to apply.** When testing anything interpolated, look for the inputs where the interpolation collapses
to an identity and assert there first:

- fraction 0 or 1 on a lerp or a spline returns an endpoint;
- a degenerate pair (`Older == Newer`) holds a value;
- a reset's three same-changetime entries make `dt2` zero, so the curve is linear.

Those points are oracles: the right answer is a number already in the data, not something a model has to
predict. Everything else about the curve is a second question, and mixing the two produces a measurement
that cannot fail for the fault it names.

**And the fixture for such a test has to be extreme enough to reproduce the fault.** The first one here
corrected a clock by one tick; sabotaging the guard reddened nothing. The fault needed flushed changetimes
far ABOVE the live ones following them, so a binary search lands low and no cheaper clause can climb past.
See [[most-of-a-decoder-is-untested]] — a sabotage that reddens nothing names the missing input.

Related: [[the-prune-keeps-two-stale-entries]], [[the-interpolation-pair-is-found-by-changetime]],
[[port-the-engines-bottom-layer-first]], [[a-picture-is-assertable]].
