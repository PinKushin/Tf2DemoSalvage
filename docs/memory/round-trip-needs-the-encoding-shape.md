---
name: round-trip-needs-the-encoding-shape
description: Which optional fields a message sent is not recoverable from the decoded values; the decoder has to record it or the demo cannot be rebuilt.
metadata:
  type: project
---

A delta-coded message decodes to values, and the values do not say which fields were on
the wire. Re-encoding by the obvious rule — send a field when it differs from the previous
record — is wrong, and it is wrong in a way that only a bit comparison against the original
can see.

Measured on `svc_Sounds`, 2026-08-10: that rule came out **exactly 12 bits short per
occurrence** across hundreds of corpus bodies, always a multiple of an origin's 12-bit
width. The engine compares positions at full precision; the decoder sees them quantised to
an 8-unit grid; two sounds from one moving entity land in the same cell, so the field looks
redundant when the sender did not think so. The same applies to any field the wire
quantises, and to a *form* choice — a narrow entity index, a sequence sent as "one higher"
rather than in full.

So a lossless decoder records the encoding shape alongside the values.
`DecodedSound.Sent` is a `SoundFields` mask doing exactly that, and adding it took the
round trip from hundreds of mismatches to zero across 11,989 sounds and five protocols.

**How to apply:** when writing an encoder for any delta-coded message here, do not infer
presence — carry it. And pick the sabotage carefully: narrowing a width that decoder and
encoder share still round-trips through *values*, and fails only against the original
demo's bits. Comparing against the original is what makes a round trip evidence rather
than a tautology. Related: [[differential-beats-fixtures]],
[[read-the-encoder-not-the-decoder]], [[numeric-decoding-traps]].
