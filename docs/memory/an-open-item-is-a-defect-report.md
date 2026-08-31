---
name: an-open-item-is-a-defect-report
description: A "still to read" note that predicts a symptom is an unfixed bug; read it before forming a new theory.
metadata:
  type: feedback
---

`docs/RISKS.md` carried this for months, first on a list headed **Still to read**:

> the `update_baseline` flag and the two baseline slots. This parser ignores both, and **a baseline
> swap that changes how a later delta is interpreted would look exactly like this.**

It was a correct diagnosis. In the meantime the same symptom — spawn props drawing in the wrong
place or not at all — was blamed on entity parenting, on render mode, and on PVS. Three
investigations, two merges reverted.

**Why:** the note was filed under a heading that reads like optional background, inside the write-up
of a bug that had already been fixed. Nothing said *this is broken now*. Every wrong theory was also
a real Valve mechanism we had half of, so each produced a plausible story and some genuine fixes.

**How to apply:** when a symptom matches the TEXT of an open item, read that item before forming a
new theory — not after the new one fails. Grep `docs/RISKS.md`, the "still to read" tails, and
`docs/findings/` for the symptom's own words. And when filing: a mechanism we do not implement is an
open defect and belongs in a numbered entry a symptom search will surface, not in the tail of a
closed investigation.

More measurement would not have helped: all three wrong theories rested on correct measurements of
correctly decoded values. See [[read-the-spec-before-measuring-our-data]],
[[a-bug-is-a-divergence-search-first]], [[half-a-mechanism-is-not-parity]],
[[decoding-a-field-is-not-honouring-it]] — `baseline` and `update_baseline` were decoded and
round-tripped for months with no consumer.
