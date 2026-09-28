---
name: bsp-lumps-are-compressed
description: Every lump of a shipped TF2 map is LZMA packed, the directory never says so, and reading raw yields plausible numbers.
metadata:
  type: project
---

Every lump of a shipped TF2 `.bsp` — geometry AND entity text — is LZMA compressed. The lump
directory's offset/length/version are identical whether compressed or not; only a 17-byte header
inside the lump (`'LZMA'`, decompressed size, packed size, five property bytes) announces it. The
standard `.lzma` 8-byte size field is absent — this header replaces it.

**Reading raw doesn't fail, it produces numbers.** Compressed bytes read as `dface_t` gave face 0 a
plane index of 23,116 of 1,824 planes — caught only because a bounds check happened to exist. The
entity lump is worse: compressed bytes contain no `{`, so a text parse returns zero entities cleanly,
indistinguishable from an empty map — cost a wrong conclusion before being noticed.

**The check that identifies it costs nothing** — see [[length-arithmetic-identifies-a-layout]]. A
fixed-size-struct lump's length is a whole multiple of stride: `dface_t` is 56 bytes, faces lump
147,154 = 2,627.75 entries (wrong); decompressed 773,976 = exactly 13,821. Use `%` and refuse;
`count = length / stride` silently turns "not a face lump" into a face count.

Decoding via the LZMA SDK (public domain, `SevenZip`, 56KB). Two measured quirks: output size isn't a
hard stop (a match beginning below the limit overshoots by up to 273 bytes, breaking an
exactly-sized `MemoryStream`); it raises its own exception types including a bare
`InvalidOperationException`.

**`SharpCompress` is not an option here** — its `public BitReader` in the global namespace displaced
this project's own `BitReader` everywhere via namespace resolution order, breaking Core's compilation
on the package reference alone.
