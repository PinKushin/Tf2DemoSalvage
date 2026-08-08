---
name: numeric-decoding-traps
description: Float rounding, derived square roots, and signed-vs-unsigned bit ranges — the arithmetic traps in this decoder, all of which fail as plausible numbers rather than errors
metadata:
  type: project
---

Three arithmetic traps in `SendPropDecoder`, recorded 2026-08-08. They share a failure mode:
**none of them throws.** Each produces a number that looks entirely reasonable, which is why
they need tests aimed at them specifically rather than end-to-end coverage.

## Deriving z from a normal, and why the clamp is not only about bad data

A unit normal transmits x and y plus a sign bit; z is reconstructed as
`sqrt(1 - x² - y²)`. The clamp guarding that square root matters more than it looks:

- **Float rounding alone can push `x² + y²` above 1**, even for a legitimately unit-length
  normal. The components are quantised to 11 bits each, so they are already approximations,
  and squaring amplifies the error.
- `sqrt` of a small negative is **NaN**, and NaN propagates silently through every later
  calculation. It does not throw, it does not stop anything, and it turns up much later as a
  position that will not render.

So the guard covers ordinary rounding at least as much as malformed input. An earlier comment
in the code called it "malformed rather than impossible", which undersold it.

**The subtler trap, found by mutation testing:** every normal-vector test originally written
happened to produce **z = 0**. With z pinned at zero, neither the sign bit nor the square-root
arithmetic is observable — mutating `1f - squared` to `1f + squared` changed nothing any test
could see. Use components with real slack (0.5 and 0.5 give z ≈ 0.707) so the value is
non-zero and the sign is testable.

## Signed versus unsigned is a range decision that becomes a memory cost

In this wire format, sign costs **no extra storage**: Source transmits N bits either way and
the sign is just how the top bit is read, two's complement. What it costs is **range** — an
11-bit signed property spans −1024…1023 rather than 0…2047. That is why `SPROP_UNSIGNED`
exists as a per-property flag.

The owner's framing is the sharper one and worth keeping: *range loss becomes a memory cost the
moment you need the range.* To hold 65,535 in a signed type you must move to 32 bits, because
16-bit signed stops at 32,767. Half the reach for the same bits means double the bits for the
same reach.

**The decoding trap:** an 11-bit `-1` read without sign extension comes back as **2047**. Not a
crash — a plausible number. `SendPropDecoder.ReadInt` sign-extends by shifting up and back
(`(int)raw << shift >> shift`), and there is a test for negative values at several widths
because nothing else would catch it.

**Owner's habit, and why it does not apply here:** default to unsigned whenever a negative is
neither wanted nor expected, since it buys back the range for free. That reasoning is sound and
simply does not fit this domain — Source coordinates are genuinely signed (the world spans
roughly ±16,384 units either side of the origin), as are velocities, punch angles and view
offsets. The schema decides per property, and the decoder must honour whichever it says rather
than assuming.

## Range-encoded floats: test both ends and the middle

A range-encoded value is a fraction of the way from `LowValue` to `HighValue`. Testing only the
lower endpoint proves nothing: at raw 0 the span is multiplied by zero, so a decoder that
*added* the bounds instead of subtracting them returns the correct answer there anyway.
Mutation testing caught precisely that. Test both endpoints and a midpoint, over a range that
crosses zero.

See [[fixtures-are-the-weak-point]] — these were all found because the fixtures were the weak
link, not the code.
