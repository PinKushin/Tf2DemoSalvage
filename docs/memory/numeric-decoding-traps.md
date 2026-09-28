---
name: numeric-decoding-traps
description: Float rounding, derived square roots, and signed-vs-unsigned bit ranges — the arithmetic traps in this decoder, all of which fail as plausible numbers rather than errors
metadata:
  type: project
---

Three arithmetic traps in `SendPropDecoder`. None throws — each produces a plausible-looking number.

**Deriving z from a normal:** z = `sqrt(1 - x² - y²)`. Float rounding alone can push `x² + y²` above 1
even for a legitimately unit normal (11-bit quantised components), and `sqrt` of a small negative is
NaN, propagating silently. The clamp guards ordinary rounding, not just malformed input.

**Subtler trap, found by mutation testing:** every normal-vector test happened to produce z = 0,
where neither the sign bit nor the sqrt arithmetic is observable — mutating `1f - squared` to `1f +
squared` changed nothing. Use components with real slack (0.5, 0.5 gives z ≈ 0.707).

**Signed vs. unsigned is a range decision, not a storage cost** — sign costs no extra bits (two's
complement), but halves the range, hence `SPROP_UNSIGNED` as a per-property flag. **The decoding
trap:** an 11-bit −1 without sign extension comes back as 2047 — plausible, not a crash.
`SendPropDecoder.ReadInt` sign-extends via `(int)raw << shift >> shift`.

**Default-to-unsigned doesn't apply here** — Source coordinates/velocities/angles are genuinely
signed; the schema decides per property.

**Range-encoded floats need both ends AND a midpoint tested** — at raw 0 the span multiplies by zero,
so a decoder that ADDS bounds instead of subtracting returns the correct answer there anyway (caught
by mutation testing).

See [[fixtures-are-the-weak-point]] — all found because fixtures were the weak link, not the code.
