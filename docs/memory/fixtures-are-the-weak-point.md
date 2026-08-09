---
name: fixtures-are-the-weak-point
description: Hand-written test fixtures caused more bugs here than the decoders did — prefer round-trip properties (CsCheck) where an encoder exists
metadata:
  type: project
---

**In this project the least reliable part of the test suite has been the fixtures, not the
code they test.** Recorded 2026-08-08 after it happened four times.

Actual fixture bugs, each of which looked like a decoder bug first:

- Byte-aligning one message and appending another. `Build()` pads to a byte boundary, padding
  is 0–7 bits, a message type field is 6 — so the reader consumes a type field spanning the
  padding *and* the next message. Symptom: the second message simply is not found.
- Forgetting that trailing zero padding decodes as `net_NOP`, because NOP is message id 0.
  Message counts came out one or two higher than expected.
- Hand-computing an expected value wrongly (a `net_Tick` count, a substring boundary).
- `ShouldNotContain("#     1")` — an assertion that could never match anything, because the
  listing pads row numbers to eight columns. It passed whether or not the feature worked.

**The fix is round-trip properties.** Encode an arbitrary value, decode it, require equality.
There is no hand-computed expectation to get wrong. CsCheck (D12) is wired up on `BitReader`
and `VarInt`; extending it to the codecs is worth doing per codec, since each needs an encoder
written from the format description.

**Do not oversell it.** A fault injected into `VarInt` — 6-bit groups instead of 7 — was caught
by the CsCheck properties *and* by the existing hand-written tests, which failed 14 of 30. For
a fault that breaks every value, both work. The property tests win on faults that break only
*some* values: hand-written tests check chosen points (0, 1, 127, 128, 300, `uint.MaxValue`)
and a bug at exactly 2^28, or in 64-bit values with bits in both halves, sits between them.
Shrinking and reproducible seeds are conveniences on top, not the justification.

**Practical note:** SonarAnalyzer raises S2699 ("no assertion") on CsCheck tests because it
does not know `Gen.Sample` throws on falsification. Suppress at class scope with a reason, not
project-wide.

See [[tests-before-codecs]] — the other half of the same lesson, about ordering rather than
technique.

## Derived widths: `floor(log2(n)) + 1`, and why every fixture agreed with `ceil`

Class ids and array counts are sized from a count rather than transmitted. The width is
`floor(log2(count)) + 1`. A `ceil`-based implementation shipped and passed **every** test in
the file, because the fixtures used two classes — and at 2, and at every exact power of two,
the two formulas give the same answer. The first evidence came from a real demo: 362 classes
must be 9 bits, and `ceil` said 10.

This is the *wrong condition* failure from the testing doctrine, not a weak assertion. The
assertions were fine. The inputs were ones where correct and broken predict the same
observation. The fix was to add rows — 3, 362, 363 — that actually separate them, not to
assert harder about 2.

**Fixtures and the corpus measure different things, and one cannot substitute for the other.**
A fixture built from the SDK's write path proves the decoder matches *that reading of the
spec*. It cannot prove the reading is right, because both sides came from the same head. Only
a real demo tests that, and when the two disagree the demo wins. Entity decoding currently
passes every fixture and desynchronises inside `CTFPlayer` on real files — see RISKS B12.

## A fixture that parses to *nothing* is the common failure, not one that throws

Three more fixture bugs on 2026-08-09, all producing a silently empty result rather than an
error:

- A `userinfo` string table entry written with an invented bit layout instead of the one
  `StringTableCodec` reads: index bit, has-text bit, substring bit, text, user-data bit, length.
  Parsed to zero entries.
- The same table's length written as a varint, when the dumper's decode state has seen no
  `svc_ServerInfo` and therefore reports protocol 0 — which takes the fixed 20-bit path. Parsed
  to zero entries.
- A game event fixture whose `svc_GameEventList` arrived in the same command as the events. A
  game event carries only an id, so without a prior definition it decodes to nothing.

None threw. Each produced an empty section that looked like "this demo has no players" rather
than "this fixture is wrong", and each cost a debugging cycle that started by suspecting the
decoder.

**Write the fixture from the reader, not from memory.** Open the parsing code and mirror its
field order. Every one of these came from writing what the format "should" be.

**And assert the fixture produced something**, before asserting anything about its content. A
test that checks `dump.ShouldContain("userid=Sassy")` fails identically whether resolution is
broken or the table was never parsed.

## Practical: the Edit tool takes literal text, Python does not

Getting a string containing `
` into a C# file through a Python heredoc took four attempts,
twice. Each layer — shell, heredoc, Python string, C# source — treats a backslash as something
to interpret, and the failure mode is a literal newline inside a string constant.

The Edit tool passes text through unchanged. Use it for anything containing escapes, raw string
literals, or exact expected output. Reach for Python only for mechanical whole-file
transformations with no escaped characters in them.
