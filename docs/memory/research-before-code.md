---
name: research-before-code
description: The project's method — hypothesise, then check first sources and decomp BEFORE coding or testing, confirm the sources don't already answer it, then test.
metadata:
  type: feedback
---

The owner's stated loop, verbatim in intent: **hypothesis → research from first sources or decomp →
refine hypothesis → make sure that hypothesis isn't already answered in the sources or decomp →
test it.**

"Always check source first." Applies to test oracles and claims about behaviour, not just to
implementation. Valve publishes far more than expected, and it is usually one fetch away:
`studio.h`, `optimize.h` (NOT `optimized_model.h`), `hardwareverts.h`, `bitbuf.cpp`,
`mathlib_base.cpp`, and the tools that WRITE the formats — `vradstaticprops.cpp` writes the `.vhv`
files. Decomp is the fallback when nothing is published, and its output never enters a repository.

**Why:** skipping the verify step is what costs, every time. Two failures in one session, both
self-inflicted:

- A "better" manifold measurement for `.vtx` was written into a test comment claiming "three orders
  of magnitude of separation" **before being measured**. Measured: 44.3%, and the sabotage it was
  meant to catch passed. The test was deleted.
- A `.vhv` test oracle compared the flattened colour count against the `.vvd`'s LOD-0 vertex count.
  It disagreed on one model and read as a reader defect. Reading `vrad`'s `SerializeLighting` —
  one fetch — showed it writes a `MeshHeader_t` per mesh per LOD counted from the **strip group's**
  vertices. The reader had been right the whole time; the oracle was wrong.

**How to apply:** before writing a test's expected value, ask what wrote the file and whether that
writer is published. Prefer the encoder to the decoder. State the hypothesis, then go looking for
the passage that settles it, and only then write code. Related:
[[read-the-encoder-not-the-decoder]], [[differential-beats-fixtures]],
[[valve-publishes-bitbuf]], [[binaries-answer-what-the-sdk-cannot]].
