---
name: fixtures-are-the-weak-point
description: "Hand-written fixtures caused more bugs here than the decoders did — prefer round-trip properties, and source every fixture from a real specimen or the SDK, never from our own code."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:35:49.336Z
---

**In this project the least reliable part of the test suite has been the fixtures, not the code they
test.** Recorded 2026-08-08 after it happened four times: byte-alignment miscounted between two
messages; trailing zero padding decoded as `net_NOP` and inflated counts; hand-computed expected
values were wrong; an assertion that could never match anything (row-padding mismatch) passed
regardless of whether the feature worked.

**The fix is round-trip properties** — encode arbitrary value, decode, require equality, no
hand-computed expectation to get wrong. CsCheck (D12) is wired to `BitReader`/`VarInt`. A fault
breaking every value is caught by both approaches; property tests win on faults breaking only SOME
values, since hand-written tests check chosen points and a bug at exactly 2^28 sits between them.

## Derived widths: `floor(log2(n)) + 1`, and why every fixture agreed with `ceil`

Class ids are sized `floor(log2(count)) + 1`. A `ceil`-based implementation passed every test because
fixtures used two classes, where `ceil` and `floor` agree (as at every exact power of two). A real
demo's 362 classes needed 9 bits; `ceil` said 10. **Fixtures and the corpus measure different
things** — a fixture built from the SDK's write path proves the decoder matches THAT reading, not
that the reading is right; only a real demo does, and it wins when they disagree. Entity decoding
passed every fixture and desynchronised inside `CTFPlayer` on real files (RISKS B12).

## A fixture that parses to NOTHING is the common failure, not one that throws

Three fixture bugs producing silently empty results: an invented bit layout for a userinfo string
table entry (parsed to zero); a length written as varint when the fixture's protocol was actually 0
(fixed 20-bit path expected); a game event fixture missing its definition command. **Write the
fixture from the reader, not from memory** — open the parsing code and mirror its field order. Assert
the fixture produced something before asserting on its content.

---

## `put-the-real-file-in-the-fixture` — the axis is WHICH REFERENCE, not synthetic-vs-real

**A fixture authored from the same belief as the reader can't falsify that belief.** Three bugs
survived exactly this way: a 13-byte cubemap record (builder and reader agreed, both wrong — real is
16); a `Patch` VMT test with keys at the wrong nesting level (real VMTs never use that shape); a
coverage denominator scraped only the axes already known.

Owner's correction: *"use the actual read demos to make the synthetic tests, you dont assume our code
when doing that... the sythetic fixtures can actually test things we should never see in a real
demo... when you use the correct reference for the fixture, either a real demo or the sdk, or the
decompiler, and not our code, then you avoid it."*

**The question is never "is this fixture synthetic" — it's "where did its bytes come from?"** Four
acceptable answers: a real specimen, the SDK, a decompilation, or arithmetic on one of those. Our own
code is not on the list.

**Synthetic fixtures are still required, for two reasons:** Stryker mutation reruns can't afford
opening real demos; and synthetic data reaches cases no real demo contains (malformed lengths,
maximum values) — [[author-the-specimen-the-corpus-lacks]] is the same point from the writer's side.

**Diagnosis still wants the real file** — that's where a wrong belief gets falsified. Where synthetic
is unavoidable, assert a property REAL data satisfies that a wrong reading cannot (e.g. the ±16384
world bound for cubemaps, not a count).

Story: `docs/findings/27-cubemap-placement.md`.

---

## Authoring one: use Edit, and nothing else

Getting an escape sequence into a C# file through a scripted heredoc failed four times across two
attempts — Edit passes text through unchanged, the only correct route. See
[[edit-files-with-the-file-tools]].

---

## `differential-beats-fixtures` — a fixture cannot falsify your own reading

Flattened property order was wrong for weeks, every fixture passed, died in a single diff against
`demostf/parser`. Two bugs, both invisible to every fixture: `ClassIdBits` used `ceil` not `floor`
(agree at 2 classes and every power of two; a real demo's 362 needs `floor`); the changes-often
partition was a SWAP not a stable partition (both forms agree on the head, diverge in the tail no
test asserted).

Both are the *wrong condition* failure — inputs where correct and broken predict the same
observation. `tools/differential/` dumps each class's flattened list from both parsers; a wrong index
reads a real value into the wrong field silently, never fails outright. First diff: 741 properties for
`CTFPlayer`, identical name sets, order diverging at index 20 — cleared the schema parser, exclusion
rules, array expansion simultaneously.

**A demo from another era is a differential too.** Protocol 14's `dem_datatables` threw at the payload
tail, looking like an off-by-one; the parse actually got one table where the 2009 demo gets 334.
Diffing the two eras' parses of the SAME table found a **one-bit** difference in four steps (both
start identically, costs match through property 0-1, raw bits pinpoint one bit, field accounting
leaves only the bit-count width as candidate) — confirmed against `svc_ServerInfo`'s `max_classes 216`
elsewhere in the same file.

**A clean check that cannot see the failure is not evidence.** A demo was reported decoding cleanly via
`--trace` (no stop markers), but `--trace` without `--entities` never parses the schema — structurally
blind to the broken half. The identical error was repeated hours later on a protocol-11 demo whose
truncated SourceTV schema had never been parsed. **After `--trace`, the schema is still unverified** —
use `--entity-limit 1` or run the corpus suite.

**The rule:** when an independent implementation exists, build the comparison BEFORE trusting a
subsystem whose errors are silent. See [[layer2-is-a-dependency-chain]].

---

## `two-recordings-of-one-value` — a differential without a second parser

A demo stores the recording player's view angles TWICE: `democmdinfo_t` (plain floats) and
`dem_usercmd` (bit-packed). Neither path can see the other. Measured: 329,969 of 330,853 packets
(99.7%) carry angles bit-identical to the last user command.

**Why:** fixtures and round-trip properties both test a codec against the same interpretation that
produced it. A second, unrelated encoding of the same quantity — written by the engine, not us — can
falsify a misreading shared by both halves.

**How to apply:** before writing a new decoder, look for the value elsewhere in the file by a
different route. Compare as BITS, not with tolerance — nothing computes these values, so an epsilon
only hides a real disagreement. Expect a rate, not equality, at different sampling frequencies, and
state the measured rate.

Related: [[read-the-encoder-not-the-decoder]].

---

## `real-data-hides-bugs-small-inputs-expose` — write the test at the smallest distinguishing size

A bug a real map/demo can't expose isn't rare — its symptom is CANCELLED by input size. Map-clustering
marked grid occupancy at segment ENDPOINTS only, splitting a long edge's connected region; real maps'
vertex density hides it (a vertex lands in nearly every cell), but three 500-unit-edge quads split
immediately. A `playlist.Items.Count` test kept passing after control moved to virtual mode (empty
`Items`) — right for a different, unstated reason.

**Why:** real data's density supplies missing behaviour by accident, so the measurement reports
"correct" while measuring nothing.

**How to apply:** when a rule is derived from real files, write the unit test at the SMALLEST size
where correct and broken differ, not a realistic one. Ask what the real input supplies that a minimal
one wouldn't — "enough points that the gap never appears" is the test to write.

Related: [[measure-the-output-not-the-capability]].
