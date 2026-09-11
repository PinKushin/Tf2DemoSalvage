---
name: fixtures-are-the-weak-point
description: "Hand-written fixtures caused more bugs here than the decoders did — prefer round-trip properties, and source every fixture from a real specimen or the SDK, never from our own code."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:35:49.336Z
---

**In this project the least reliable part of the test suite has been the fixtures, not the
code they test.** Recorded 2026-08-08 after it happened four times.

**`put-the-real-file-in-the-fixture` was merged into this one on 2026-08-27** — it is the answer to
the question this entry raises, so keeping them apart meant reading the problem without the rule.

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

---

## `put-the-real-file-in-the-fixture` — the axis is WHICH REFERENCE, not synthetic-vs-real

**When a real example of a format is available, put the real bytes in the fixture.** A fixture the
same person authored from the same belief as the reader cannot falsify that belief, however many
cases are written on top of it. Three bugs in one session (2026-08-18) survived their tests exactly
this way:

| Bug | The fixture that confirmed it |
|---|---|
| `dcubemapsample_t` read at 13 bytes instead of 16 | builder emitted 13-byte records |
| `Patch` VMTs were a total no-op — `replace` block keys dropped | test put the keys at the patch's **top level**, a shape real VMTs never use |
| `SdkCoverageTests` missing the standard-var axis | denominator scraped only the two axes already known |

Each had *more* tests than average. The patch one had a test named
`Patch_TakesTheIncludedTextureAndTheOverriddenKeys` that passed while no patch anywhere applied.
The failure is not a careless fixture, it is a faithful one — a correct expression of the wrong
model. Adding cases multiplies the same hypothesis; it never tests it.

**Owner's correction, 2026-08-21**, when this was read as "prefer real files":

> *"what im telling you to do is use the actual read demos to make the synthetic tests, you dont
> assume our code when doing that … plus the sythetic fixtures can actually test things we should
> never see in a real demo, so we can catch edge cases we wouldnt otherwise be able to with real
> demos. So tension, sort of, but when you use the correct reference for the fixture, either a real
> demo or the sdk, or the decompiler, and not our code, then you avoid it"*

**The defect in every row above is not that the fixture was authored. It is that it was authored
from THIS PROJECT'S BELIEF.** A 13-byte cubemap record and a 13-byte reader agree because one person
wrote both; a fixture built by reading the real file, or `bspfile.h`, would have been 16 and the
reader would have gone red on its first run.

**So the question is never "is this fixture synthetic". It is "where did its bytes come from?"** —
and there are exactly four acceptable answers: a real specimen, the SDK, a decompilation, or
arithmetic on one of those. Our own code is not on the list.

**Synthetic fixtures are required here, for two reasons neither of which is convenience:**

- **Mutation runs cannot use real demos.** Stryker re-runs the suite per mutant, and a suite that
  opens 774 MB of demos slows to a crawl. Synthetic inputs are what make mutation testing possible
  at all — and mutation testing is a standing requirement (`CLAUDE.md`).
- **They reach cases no real demo contains.** A malformed length, a field at its maximum, a
  combination the engine never writes. Those are exactly the inputs a decoder gets wrong, and no
  corpus will ever supply them. [[author-the-specimen-the-corpus-lacks]] is the same point from the
  writer's side.

**And diagnosis still wants the real file.** When something is actually broken, open a real demo —
that is where a wrong belief gets falsified, and it is the step that did not happen in any of the
three cases above.

- **Real bytes where a real file exists.** cp_process_final's pakfile, the corpus demos, an SDK
  header. `VmtPatchBlockTests` embeds a patch VMT byte for byte.
- **Where synthetic data is unavoidable, assert a property of REAL data that the wrong reading
  cannot satisfy** — not a count, which is as plausible either way. For cubemaps that was the
  ±16384 world bound; a stride error lands outside it and a correct one cannot.
- **A fix whose failure mode is "works on everything I tried" needs its negative case written
  down.** Flattening `replace` had to keep NOT flattening `Proxies`, and a `replace` nested inside
  `Proxies` is still a proxy's.

Story: `docs/findings/27-cubemap-placement.md`.

---

## Authoring one: use Edit, and nothing else

Getting a string containing an escape sequence into a C# file through a scripted heredoc took four
attempts, twice. Each layer — shell, heredoc, script string, C# source — treats a backslash as
something to interpret, and the failure mode is a literal newline inside a string constant.

**The Edit tool passes text through unchanged, so it is the only correct way to write a fixture.**
An earlier version of this paragraph ended "reach for Python only for mechanical whole-file
transformations"; that exemption no longer exists and Python specifically is rejected — see
[[edit-files-with-the-file-tools]].

Related: [[struct-padding-is-on-disk]], [[output-level-assertion-or-it-is-not-done]],
[[instrument-bugs-outnumber-decoder-bugs]].

---

## `differential-beats-fixtures` — a fixture cannot falsify your own reading

The flattened property order was wrong for weeks. Every fixture passed. It died in a single
diff against `demostf/parser`, recorded 2026-08-08.

### Why the fixtures could not catch it

A fixture built from the SDK's write path proves the decoder agrees with **that reading of the
spec**. It cannot prove the reading is right, because both sides came out of the same head. The
tests here were not weak — they asserted exact values and were confirmed to fail when the code
was deliberately broken. They were simply blind to the one thing nobody thought to question.

Two bugs of exactly this shape, both invisible to every fixture in the repo:

- **`ClassIdBits` was `ceil(log2(n)) + 1`; the wire uses `floor`.** At 2 classes — what every
  fixture used — the formulas agree. They also agree on every exact power of two. A real demo's
  362 classes needs 9 bits and `ceil` said 10.
- **The changes-often partition is a *swap*, not a stable partition.** Both forms put
  changes-often properties first, in the same order. Only the *tail* differs, and no test
  asserted the tail. One test explicitly claimed stability was the contract and reasoned that
  an unstable sort would corrupt the addressing — the reasoning was backwards.

Both are the *wrong condition* failure: inputs where correct and broken predict the same
observation. The fix is never a stronger assertion, it is an input that separates them.

### What the differential actually buys

`tools/differential/` dumps each class's flattened list, in index order, from both parsers. The
comparison is decisive in a way no single-parser test can be, because a wrong index reads a
real value into the wrong field — it never fails, it just quietly describes a different match.

The first diff narrowed the search enormously without any debugging. Both lists held 741
properties for `CTFPlayer`, 235 of them array elements, with **identical sets of names** and
order diverging at index 20. That one fact cleared the schema parser, the exclusion rules and
array expansion simultaneously — each of those would have changed the *set* — and pinned the
fault to the final partition.

**Diff every class, not one.** The ordering rules only diverge on particular table shapes.
`CTFPlayer` alone going green would have proved much less than 204,000 properties across four
demos going green.

### The second parser is not the only differential — a demo from another era is one too

2026-08-10, protocol 14: `dem_datatables` threw at the very end of an 85,848-byte payload, which
reads like an off-by-one at the tail. It was not. **The parse got one table where the 2009 demo
gets 334** — the desync was immediate and the last bit consumed only said where the wandering
stopped. *Where a bit-stream parser dies is almost never where it went wrong.*

Diffing the two eras' parses of the **same table** found it in four steps, no debugger:

1. Both start with `DT_AI_BaseNPC`, 12 properties — so the tables are comparable.
2. Properties 0 and 1 cost identical bits in both (285, 188) — so the reader was still
   synchronised at property 2, and the fault is inside property 1's fields.
3. The raw bits gave the size: protocol 14 at bit 597 holds what protocol 15 holds at bit 598.
   **One bit.**
4. `188 = 5 + 96 + 16 + 32 + 32 + 7` accounts for every other field of property 1, leaving the
   bit-count width as the only candidate. It is **6 below protocol 15, 7 from 15 on**.

**Confirm against something the hypothesis did not touch.** At six bits the schema yields 216
server classes; `svc_ServerInfo` elsewhere in the file reports `max_classes 216`. Two unrelated
parts of one file agreeing is what separates a measurement from a fitted answer.

### A clean check that cannot see the failure is not evidence

The same demo was first reported as decoding end to end. The trace was genuinely clean — 12,608
commands, no stop markers — but `--trace` without `--entities` **never parses the schema**, so the
check was structurally blind to the half that was broken. The claim was made and committed before
the corpus tests ran.

**Choosing where to look is part of the measurement**, and a passing check whose instrument cannot
reach the failure is worth nothing. Related: [[ask-whether-the-data-arrived]].

**I then made the identical error again, hours after writing the paragraph above.** A protocol-11
demo arrived, I ran `--trace`, grepped for stop markers, found none, and reported "zero decode
failures". Its SourceTV schema was truncated at 64 KiB and had never been parsed. The corpus tests
caught it minutes later.

Knowing an instrument's blind spot does not make you check for it — the check has to be attached
to the action. So: **after `--trace`, the schema is still unverified. Use `--entity-limit 1`, or
run the corpus suite.** Two eras in one day failed exactly here.

### The rule

When an independent implementation exists, build the comparison **before** trusting a
subsystem whose errors are silent — not after the fixtures stop finding anything. Fixtures
verify internal consistency; only a second implementation verifies the reading.

See [[layer2-is-a-dependency-chain]] for why this subsystem could not be checked end to end any
earlier.

---

## `two-recordings-of-one-value` — a differential without a second parser

A demo stores the recording player's view angles **twice**: `democmdinfo_t` writes them as plain
little-endian floats ahead of every packet, and `dem_usercmd` writes them bit-packed behind presence
bits. Neither path can see the other. Measured 2026-08-11: **329,969 of 330,853** packets carry
angles bit-identical to the last user command before them, 99.7%.

**Why:** fixtures and round-trip properties both test a codec against the same interpretation of the
spec that produced it — they cannot falsify a misreading shared by both halves. A second, unrelated
encoding of the same quantity can, because the engine wrote both and this project wrote neither.
It is the differential above without needing another parser.

**How to apply:** before writing a new decoder, look for whether the value appears anywhere else in
the file by a different route. Compare as **bits**, not with a tolerance — nothing computes these
values, so an epsilon only widens the test enough to hide a real disagreement. Expect a rate rather
than an equality when the two are sampled at different frequencies (input is sent faster than
snapshots), and state the measured rate in the test so a drop is visible.

Related: [[read-the-encoder-not-the-decoder]].

---

## `real-data-hides-bugs-small-inputs-expose` — write the test at the smallest distinguishing size

A bug that a real map or a real demo cannot expose is not a rare bug — it is a bug whose symptom
is *cancelled by the size of the input*. Two of them, found 2026-08-12:

- The map-clustering code marked grid occupancy at segment **endpoints** only, so a long edge
  skipped the cells between and split one connected map into pieces. On a real map, vertex density
  means some vertex lands in almost every cell, so it never split anything. Three quads with
  500-unit edges split immediately.
- A test asserting `playlist.Items.Count` continued to pass after the control moved to virtual
  mode, where `Items` is empty — it happened to still be right for a different reason, but nothing
  in the test said which reason it was measuring.

**Why:** the density of real data supplies the missing behaviour by accident. The measurement then
reports "correct" while measuring nothing — the same failure this file opens with, approached from
the other side: there the fixture was wrong, here the fixture is the only thing that can be right.

**How to apply:** when a rule is derived from a measurement over real files, write the unit test at
the smallest size where correct and broken differ, not at a realistic one. Ask what the real input
is *supplying* that a minimal one would not. If the answer is "enough points that the gap never
appears", that is the test to write.

Related: [[measure-the-output-not-the-capability]].
