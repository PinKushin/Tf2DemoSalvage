---
name: differential-beats-fixtures
description: Fixtures written from your own reading of a spec cannot falsify that reading — the flattening order bug passed every fixture for weeks and died in one diff against another parser
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-10T12:12:55.604Z
---

The flattened property order was wrong for weeks. Every fixture passed. It died in a single
diff against `demostf/parser`, recorded 2026-08-08.

## Why the fixtures could not catch it

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

## What the differential actually buys

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

## The second parser is not the only differential — a demo from another era is one too

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

## A clean check that cannot see the failure is not evidence

The same demo was first reported as decoding end to end. The trace was genuinely clean — 12,608
commands, no stop markers — but `--trace` without `--entities` **never parses the schema**, so the
check was structurally blind to the half that was broken. The claim was made and committed before
the corpus tests ran.

**Choosing where to look is part of the measurement**, and a passing check whose instrument cannot
reach the failure is worth nothing. Related: [[ask-whether-the-data-arrived]].

## The rule

When an independent implementation exists, build the comparison **before** trusting a
subsystem whose errors are silent — not after the fixtures stop finding anything. Fixtures
verify internal consistency; only a second implementation verifies the reading.

See [[fixtures-are-the-weak-point]] for the general form, and [[layer2-is-a-dependency-chain]]
for why this subsystem could not be checked end to end any earlier.
