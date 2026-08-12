---
name: bsp-lumps-are-compressed
description: Every lump of a shipped TF2 map is LZMA packed, the directory never says so, and reading raw yields plausible numbers.
metadata:
  type: project
---

Every lump of a shipped TF2 `.bsp` — geometry **and** the entity text — is LZMA compressed. The
lump directory's offset, length and version are identical whether or not it is, so nothing
announces it except a 17-byte header inside the lump itself: `'LZMA'`, decompressed size, packed
size, five property bytes. The standard `.lzma` eight-byte size field is **absent**; the size in
this header replaces it, so the decoder must be told its output length rather than reading one.

**Reading raw does not fail, it produces numbers.** Compressed bytes read as `dface_t` gave face 0
a plane index of 23,116 out of 1,824 planes — and that only surfaced because a bounds check
happened to exist. The entity lump is worse: compressed bytes contain no `{`, so a text parse
returns zero entities, cleanly, which is indistinguishable from a map with no entities. That cost
a wrong conclusion in a measurement before it was noticed.

**The check that identifies it costs nothing** — see [[length-arithmetic-identifies-a-layout]]. A
lump of fixed-size structs has a length that is a whole multiple of that size. `dface_t` is 56 and
the faces lump is 147,154, which is 2,627.75 entries. Decompressed it is 773,976, exactly 13,821.
Use `%` and refuse; `count = length / stride` silently turns "not a face lump" into a face count.

Decoding is the LZMA SDK (public domain, `SevenZip` namespace, 56 KB). Two measured behaviours:
its **output size is not a hard stop** — a match beginning below the limit is copied whole and
overshoots by up to 273 bytes, which against an exactly-sized `MemoryStream` reports "Memory
stream is not expandable" — and it raises its own exception types including a bare
`InvalidOperationException`.

**`SharpCompress` is not an option here**, whatever its convenience: it declares a `public
BitReader` in the **global namespace**, and C# resolves the enclosing namespace chain before
`using` directives, so it displaced this project's own `BitReader` everywhere and broke Core's
compilation on the package reference alone.
