---
name: suspect-the-input-not-the-algorithm
description: "When a correct algorithm keeps producing a wrong answer, suspect the input and the identity of the value you are comparing against — not the algorithm."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-28T19:30:55.592Z
---

> **"When a perfect algorithm keeps giving a wrong answer, suspect the input and the identity of the
> thing you're measuring — not the algorithm."**

Supplied by the owner (written by another AI) after a day spent proving it the hard way.

Measured 2026-08-28. A map checksum was implemented from Valve's published description on the first
attempt and was **correct from the first attempt**. It did not match. What followed: a whole-file
variant, a file-order variant, five lump-count ceilings, an exhaustive sweep of every single extra
lump exclusion, a padding-inclusive variant, every lump alone, and finally a decompilation of the
2007 `engine.dll` — which confirmed the original implementation exactly. Two write-ups were committed
blaming the wrong thing: first the map files, then the byte selection.

The actual faults were both on the other side of the comparison. **The field was the wrong one** —
`svc_ServerInfo` carries two checksum-shaped values and the code had chased the wrong one, with its
own comment saying that branch was *"flagged rather than trusted"*. And **the engine omits
`CRC32_Final`**, so its number is the complement of a standard CRC32.

**Why single-variable search cannot find this.** With two faults present, every test that changes one
thing and holds the rest fails — and each failure reads as evidence against the variable being
tested. That is how a correct implementation gets rewritten and a correct conclusion gets abandoned.
Widening the TARGET instead — "what if the answer I want is a different number?" — cost one line and
was available from the first hour.

**How to apply:** before optimising or rewriting a computation that will not match, spend one cheap
check on each of: is this the right input file, is this the right FIELD, and is the expected value
transformed on its way to me (endianness, complement, offset, sign). Then, if it still fails, ask
whether TWO things could be wrong — because the one-at-a-time discipline that is right for a single
fault is exactly what conceals a pair. See [[instrument-bugs-outnumber-decoder-bugs]] and
[[the-denominator-decides-what-can-be-lost]].
