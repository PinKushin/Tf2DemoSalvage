---
name: research-before-code
description: "Check Valve's source before writing or changing anything, and let it outrank every other authority including the owner's recollection and mine."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:32:56.907Z
---

**Check the source first, every time, before doing anything.** Loop: hypothesis, research from first
sources or decomp, refine, confirm sources don't already answer it, then test.

**The source outranks everything, including the owner's assertions and my own reasoning** — if a
stated belief contradicts the code, say so.

Local clone: `F:\src\source-sdk-2013`, outside every repository. Measured cases from one session: a
recollection about VPK/loose-file override order was half-right for a different reason than stated,
and working code was nearly inverted to match the wrong half before the file was actually read; props
drawing at half brightness had two plausible curves fitting the measurement, and only the shader
source (`cOverbright 2.0f`) distinguished them; displacement lightmap coordinates are assigned from
corner ordering, never projected — projecting looked obviously right and broke 219 of 578 faces.

**Confirming instance at project scale:** the demo decode logic was built and believed correct BEFORE
any demo existed to test against, purely from published changelogs and earlier SDK branches, and it
worked on the first real file — research first, a specimen is corroboration, not a precondition.

**How to apply:** before writing an expected value, ask what program WROTE the file and whether it's
published — prefer the encoder to the decoder. Related: [[decode-must-be-total]],
[[read-the-encoder-not-the-decoder]], [[nothing-is-closed]], [[era-axis-is-measured]].

---

## `valve-publishes-bitbuf` — bit-level wire questions are a read, not a decompile

`ValveSoftware/source-sdk-2013` contains `src/tier1/bitbuf.cpp` — the real `bf_write`/`bf_read`
including `WriteBitCoord`, `WriteBitCoordMP`, `WriteUBitVar`. **This outranks a decompile for
anything in tier1**, and it's the encoder, which states intent — see [[read-the-encoder-not-the-decoder]].
Settled a field order and in-bounds predicate in one read after six hypotheses had been tested
against the corpus.

**Doesn't cover:** TF2-specific or engine-internal code never shipped in the SDK (demo container,
`svc_` framing, SendTable flattening) — those need the corpus, a second parser, or a disassembler.

**How to apply:** before reaching for Ghidra on a bit-level question, check whether the code is in
tier1 or public game code first. Only drop to a decompile for engine binaries.
