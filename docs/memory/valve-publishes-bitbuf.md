---
name: valve-publishes-bitbuf
description: bf_write/bf_read live in Valve's public source-sdk-2013 as src/tier1/bitbuf.cpp, so bit-level wire questions need reading, not decompiling.
metadata:
  type: reference
---

`ValveSoftware/source-sdk-2013` contains `src/tier1/bitbuf.cpp` — the real `bf_write` and
`bf_read`, including `WriteBitCoord`, `WriteBitCoordMP`, `WriteBitAngle`, `WriteUBitVar` and
the varint helpers. Fetch it with:

```bash
gh api repos/ValveSoftware/source-sdk-2013/contents/src/tier1/bitbuf.cpp?ref=master --jq .content | base64 -d
```

(The `mp/src/...` path 404s; it is `src/tier1/...`.)

**This outranks a decompile for anything in tier1**, and it is the encoder rather than a
decoder, which is the side that states intent — see [[read-the-encoder-not-the-decoder]].
`WriteBitCoordMP` settled the field order and the in-bounds predicate on 2026-08-11 in one
read, after six hypotheses had been tested against the corpus.

What it does **not** cover: anything TF2-specific or engine-internal that never shipped in
the SDK — the demo container, `svc_` message framing, `SendTable` flattening. Those still
need the corpus, a second parser, or a disassembler.

**How to apply:** before reaching for Ghidra on a bit-level question, check whether the code
is in tier1 or in the public game code. Only drop to a decompile for engine binaries.
