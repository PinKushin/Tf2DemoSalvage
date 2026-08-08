---
name: tests-before-codecs
description: Write unit tests before each decoder, not after — mutation testing caught the same lapse twice, and corpus tests cannot substitute
metadata:
  type: feedback
---

**Write the unit tests before the decoder, every time.** Established the hard way on
2026-08-07, twice in the same session.

| Codec | Tests written | First mutation run |
|---|---|---|
| `GameEventCodec` | after | 5 survivors, all in one untested helper |
| `StringTableCodec` | after | **53 survivors** in that file alone |

Both times the code passed its corpus tests and looked finished. Both times mutation
testing found the gap immediately. The second lapse happened one feature after the first,
which is why this is written down rather than merely noticed.

**Why corpus tests do not cover for it.** A real demo exercises only the paths those three
files happen to use. `StringTableCodec` has branches for fixed-size versus variable-size
user data, substring back-references, history eviction past 32 entries, explicit versus
running indices, and compressed payloads — the corpus touches perhaps half. End-to-end
tests prove the decoder works on the demos we have; they say nothing about the branches
those demos never take, and a bit-level decoder's untaken branch is exactly where a silent
misread waits.

**How to apply:** before writing a codec, write the synthetic fixture builder and the tests
for each branch the wire format describes — including the malformed cases. The builder is
reusable and is usually the harder half anyway. Then implement.

## The fixture trap that cost the most time

**Bit-level fixtures must share one continuous `BitWriter`.** Building message A, calling
`Build()`, then appending message B to a fresh writer does not work: `Build()` pads to a
byte boundary, padding is 0–7 bits, and a message type field is 6 bits. The reader then
consumes a type field spanning the padding *and* the start of message B, and desynchronises.

The symptom is confusing — B simply is not found, with no error — and it looks exactly like
a bug in the decoder. Any test asserting "what comes after message A still decodes" needs a
helper that writes *into* an existing writer rather than returning bytes. See
`StringTableCodecTests.CreateInto`.

Related: trailing zero padding decodes as a run of `net_NOP`, because NOP is message id 0.
Fixtures must expect those extra messages or filter them out — see
[[z1800-is-modern-not-2015]] for the pattern of assumptions that only real bytes disprove.
