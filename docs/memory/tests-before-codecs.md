---
name: tests-before-codecs
description: "Write unit tests before each decoder, not after — mutation testing has now caught the same lapse three times, and corpus tests cannot substitute"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:51:01.463Z
---

**Write the unit tests before the decoder, every time.** Established the hard way on
2026-08-07, twice in the same session.

| Codec | Tests written | First mutation run |
|---|---|---|
| `GameEventCodec` | after | 5 survivors, all in one untested helper |
| `StringTableCodec` | after | **53 survivors** in that file alone |
| `UserMessageBody` | after | **86 survivors** in that file alone (2026-08-12) |

Every time, the code passed its corpus tests and looked finished. Every time, mutation testing
found the gap immediately. The second lapse happened one feature after the first, which is why
this was written down rather than merely noticed — and the third happened anyway, which is worth
sitting with.

**The third one is the clearest case yet, because of how concentrated it is.** The 2026-08-12
`core` run scored 54.26 % with 242 survivors, and 86 of them — better than a third — are in
`UserMessageBody.cs` alone. The next two are `MessageAssembly.cs` (32) and `DemoAssembly.cs`
(31), both written the same way. A score that low reads like a broad quality problem and is not
one: it is three files that were written before their tests, in a codebase whose other ~40 files
are fine. **Read the survivors, not the score** — the score averages the lapse away, and the
per-file count points straight at it.

By mutator the survivors are 57 string, 40 equality, 37 statement and 35 boolean — the shape of
code whose *outputs* were never asserted precisely, only that it ran.

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
[[era-axis-is-measured]] for the pattern of assumptions that only real bytes disprove.

---

## The red step for a NEW type is a compile failure, not a failing assertion

This project's analyzers are strict enough that TDD placeholder types do not compile. With
`TreatWarningsAsErrors` plus `AnalysisMode=All` plus SonarAnalyzer, a stub whose members all throw
`NotImplementedException` fails on **CA1065** (exception from a property getter) and **S2325**
(member does not use instance state). Established 2026-08-07 when the solution was scaffolded.

**So write the tests first and then implement directly** — do not waste a cycle trying to stage a
stub, and do not relax `TreatWarningsAsErrors` or `AnalysisMode` to make one compile. The strictness
is a gate the project deliberately wants; the reasons are recorded in comments at both sites — the
analyzer settings, and every `GlobalUsings.cs`, where `System` is deliberately NOT global because the
SDK-generated `AssemblyInfo.cs` emits its own `using System;` and the pair fails as CS8933. Do not add
it there either.

**The same strictness makes a lazy sabotage impossible**, which is worth knowing before trying one:
`&& false` is S1125, dropping a call leaves a private method unreferenced (S1144), and `x = 0` on an
int field is CA1805. A sabotage must compile, so pick one that keeps every symbol used — OR-ing
`int.MaxValue` into a flag set, or `+ 500` on an index. See [[most-of-a-decoder-is-untested]].

Related: [[mutation-score-is-not-the-goal]].
