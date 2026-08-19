---
name: struct-padding-is-on-disk
description: A BSP lump record's stride is sizeof(), not the sum of its fields, and a fixture built from the wrong stride confirms it
metadata:
  type: project
---

**A lump written with `SwapLumpToDisk<T>` stores `sizeof(T)`, so C++ trailing padding is on disk.**
`dcubemapsample_t` is `int origin[3]; unsigned char size;` — thirteen bytes of content, sixteen on
disk, padded to the ints' four-byte alignment. Reading it at 13 gave a correct FIRST record and
drift after, because each later one is composed from the tail of one and the head of the next:
`(0, 0, 608)` then `(-2147483648, -2147483642, 1879048200)`.

`DECLARE_BYTESWAP_DATADESC()` inside such a struct adds nothing — `static` members and friend
templates only (`datamap.h:318`). Rule it out rather than worrying about it.

**Ten synthetic tests passed against the wrong stride**, including three specifically written to
catch a stride error, because the fixture builder was 13 bytes wide too. Tests and reader came from
one belief, so the suite was one hypothesis wearing ten assertions. Not a buggy fixture — a fixture
that faithfully expressed the bug.

**Why:** field-sum stride is right often enough to feel safe, and wrong silently. The failure
produces plausible numbers, and the first record is always correct, which is exactly what stops
anyone looking further.

**How to apply:** two checks, both cheap.

1. **Divide the real lump length by the candidate strides before writing code.** 688 bytes is
   43 × 16 exactly and is not divisible by 13. One division answers it. See
   [[length-arithmetic-identifies-a-layout]].
2. **Assert a property of REAL data that the wrong reading cannot satisfy** — not a count, which is
   as plausible either way. For placements that is the world bounds: vbsp took the positions from
   entities the compiler had already bounds-checked, so a stride error lands outside ±16384 and a
   correct one cannot.

Related: [[fixtures-are-the-weak-point]], [[real-data-hides-bugs-small-inputs-expose]],
[[instrument-bugs-outnumber-decoder-bugs]] — the first version of the falsifying test searched the
game's archives instead of the map's pakfile and found 0 of 43, which looked like the bug and was
the instrument. Story: `docs/findings/27-cubemap-placement.md`.
