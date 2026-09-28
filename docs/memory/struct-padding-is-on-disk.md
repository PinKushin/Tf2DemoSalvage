---
name: struct-padding-is-on-disk
description: "A BSP lump record's stride is sizeof(), not the sum of its fields, and a fixture built from the wrong stride confirms it"
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:52:37.129Z
---

A lump stores `sizeof(T)`, so C++ trailing padding is ON DISK. A struct with 13 bytes of content read
at 13 gave a correct FIRST record and drift after (each later record composed from the tail of one and
head of the next) — the real stride is 16, padded to alignment.

**Ten synthetic tests passed against the wrong stride**, including three written specifically to
catch a stride error, because the fixture builder was 13 bytes wide too — tests and reader shared one
belief, so the suite was one hypothesis wearing ten assertions.
`DECLARE_BYTESWAP_DATADESC()` inside such a struct adds nothing — `static` members and friend
templates only (`datamap.h:318`); rule it out rather than worrying about it.

**Why:** field-sum stride is right often enough to feel safe, wrong silently — the first record is
always correct, which is exactly what stops anyone looking further.

**How to apply, two cheap checks:**
1. Divide the real lump length by candidate strides before writing code — one division answers it.
   See [[length-arithmetic-identifies-a-layout]].
2. Assert a property of REAL data the wrong reading can't satisfy (not a count) — here, world bounds
   (a stride error lands outside ±16384, a correct one can't).

Related: [[fixtures-are-the-weak-point]]#real-data-hides-bugs-small-inputs-expose,
[[instrument-bugs-outnumber-decoder-bugs]] — the falsifying test's first version searched the wrong
archive and found 0 of 43, which looked like the bug and was the instrument. Story:
`docs/findings/27-cubemap-placement.md`.
