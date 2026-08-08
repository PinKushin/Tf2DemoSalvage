---
name: differential-beats-fixtures
description: Fixtures written from your own reading of a spec cannot falsify that reading — the flattening order bug passed every fixture for weeks and died in one diff against another parser
metadata:
  type: project
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

## The rule

When an independent implementation exists, build the comparison **before** trusting a
subsystem whose errors are silent — not after the fixtures stop finding anything. Fixtures
verify internal consistency; only a second implementation verifies the reading.

See [[fixtures-are-the-weak-point]] for the general form, and [[layer2-is-a-dependency-chain]]
for why this subsystem could not be checked end to end any earlier.
