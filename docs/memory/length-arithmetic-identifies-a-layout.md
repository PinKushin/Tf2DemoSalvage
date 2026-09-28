---
name: length-arithmetic-identifies-a-layout
description: "A message's stated bit length, and the gaps between its observed lengths, identify its layout before any byte is read."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-11T10:31:26.019Z
---

A wire message states its length in bits; that length, and the differences between lengths the same
message takes across a corpus, constrain the layout hard enough to identify it without decoding.

Worked example: protocol-14 `Damage` (RISKS B26). Bodies were 77 and 72 bits. `BitVec3Coord` is three
presence bits plus axes (22 bits with fraction, 17 without) — a full vector is 69, one bare axis 64,
leaving exactly 8 bits of header either way. Modern era lengths (118, 113) show the same 5-bit step,
proving both eras share the vector encoding.

**A fixed body length falsifies any variable-length layout outright** — 24 protocol-14 bodies were
77 bits, ruling out optional fields. **The step between two observed lengths names the optional
field.**

**Why:** a wrong layout that fits looks exactly like a right one — it produces numbers, not errors.
Arithmetic on lengths rules candidates out BEFORE they produce plausible values. HL2's `Damage`
message (the standing hypothesis) is a fixed 144 bits — one subtraction rules it out.

**How to apply:** before decoding an unknown message, histogram its stated length across the corpus.
Constant length means no optional fields; a small set of lengths means the gaps ARE the optional
fields.

**Check length with `==`, never `<=`** — these bodies end mid-byte, exact not padded. A lenient bound
accepts every layout short enough, which is how the modern layout passed for a protocol-14 body and
reported garbage. Related: [[fallbacks-do-not-make-guesses-safe]],
[[measure-the-output-not-the-capability]], [[research-before-code]], [[arithmetic-settles-disputes]].
