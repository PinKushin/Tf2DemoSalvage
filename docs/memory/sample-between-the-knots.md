---
name: sample-between-the-knots
description: A curve test that samples a control point cannot tell a spline from a lerp — every interpolation agrees at its own knots, by construction.
metadata:
  type: project
---

**Never assert a curve's shape at one of its own control points.** At a knot the interpolation
parameter is exactly 0 or 1, so the basis functions collapse and *every* scheme — Hermite, Catmull-
Rom, cosine, a plain lerp — is mathematically forced to return the stored value. The assertion reads
the table back and never touches the curve.

B348, 2026-09-05: `Degrees_AtTheMiddleControlPoint_OvershootsPastSixty` sampled `0.7519`, which IS
the middle control point's X. Replacing Valve's `Hermite_Spline` with a one-line lerp left it green —
and left the whole eight-test conformance suite green, because six of the eight never called the
function at all. **The overshoot was the entire reason the entry existed and nothing pinned it.**

**Two failure modes from `CLAUDE.md`, in one suite:**

- **Wrong condition** — the knot. Fix the INPUT: sample strictly between control points. At 0.4 the
  spline gives 34.818° where a lerp gives 33.806°, a full degree apart.
- **Effect size below resolution** — the neighbouring boundary test at `0.9999` *is* strictly
  inside a segment, but there the curves differ by 0.0014° against a 1e-2 tolerance. Being inside a
  segment is not enough; the sample has to be where the difference is large.

**How to apply, to any interpolation:**

1. Sample at a fraction with no special relationship to the control points — mid-segment, not an
   endpoint and not a knot.
2. Compute the expected value BY HAND from the engine's formula and assert it exactly. Do not read
   it off a run; that fits the test to the code.
3. State what the wrong implementation would give, in the message. `"a plain lerp gives 33.806, a
   full degree lower"` makes the margin visible instead of implied by a tolerance nobody re-derives.
4. Check the tolerance against the DIFFERENCE, not against floating-point noise.

**And count how many tests actually call the function.** Six of eight conformance tests exercised
`Spline`, `Angle` or `Fraction` directly and never `Degrees`, so a change scoped inside `Degrees`
was invisible by construction. A suite named for a mechanism is not a suite that covers it.

Related: [[most-of-a-decoder-is-untested]], [[real-data-hides-bugs-small-inputs-expose]],
[[instrument-bugs-outnumber-decoder-bugs]], [[output-level-assertion-or-it-is-not-done]].
