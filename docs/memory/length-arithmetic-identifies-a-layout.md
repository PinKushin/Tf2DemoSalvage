---
name: length-arithmetic-identifies-a-layout
description: "A message's stated bit length, and the gaps between its observed lengths, identify its layout before any byte is read."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-11T10:31:26.019Z
---

A wire message states its length in bits. That length, and the **differences between the lengths
the same message takes across a corpus**, constrain the layout hard enough to identify it without
decoding anything.

Worked example, protocol-14 `Damage` (RISKS B26, fixed 2026-08-11). Bodies were 77 and 72 bits.
A `BitVec3Coord` is three presence bits plus its axes; an axis is 22 bits with a fraction, 17
without. So a full vector is 69 and one bare axis makes it 64 — leaving **exactly 8 bits of header
either way**. One byte. The modern era's lengths are 118 and 113, the same five-bit step, which is
what says both eras share the vector encoding and differ only ahead of it.

Two things fall out that matter more than the answer:

- **A fixed body length falsifies any variable-length layout outright.** All 24 protocol-14 bodies
  were 77 bits until the last one; a layout with optional fields cannot produce that.
- **The step between two observed lengths names the optional field.** 118 vs 113 and 77 vs 72 are
  both "an axis sent without its fraction", and seeing the same step in both eras was the evidence
  they were related at all.

**Why:** the alternative is trying layouts until one fits, and a wrong layout that fits looks
exactly like a right one — it produces numbers, not errors. Arithmetic on lengths rules candidates
out *before* they can produce plausible values. The HL2 `Damage` message was the standing
hypothesis for this bug and is a fixed 144 bits; it was never a candidate for a 77-bit body, and
one subtraction says so. See [[research-before-code]] and [[arithmetic-settles-disputes]].

**How to apply:** before writing a decoder for an unknown message, histogram its stated length
across the corpus. Constant length means no optional fields. A small set of lengths means the gaps
are the optional fields, and their sizes name them. Only then read bytes.

**And check the length with `==`, never `<=`.** These bodies end mid-byte, so the stated length is
exact and not padded. A lenient bound does not tolerate rounding — it accepts every layout short
enough, which is exactly how the modern layout passed for a protocol-14 body and reported
`damage=16164`. Related: [[fallbacks-do-not-make-guesses-safe]], [[measure-the-output-not-the-capability]].
