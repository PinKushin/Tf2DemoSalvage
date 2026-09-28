---
name: read-the-encoder-not-the-decoder
description: "A reference parser's encoder states intent its decoder only implies; corpus silence about a case is not evidence the case is absent."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:52:44.740Z
---

When cross-checking a format against a reference implementation, read its ENCODER, not only its
decoder — both are one person's interpretation of the wire, but the encoder has to CHOOSE what to
emit, so a special case appears as a deliberate branch.

Example: a count byte's zero case was settled by the reference encoder's explicit `(1, Some(event))
if event.reliable => 0` — a count of 0 means one effect sent reliably, not empty. This project's
decoder looped `count` times, silently leaving the body unread. **The corpus couldn't have caught it
and its silence looked like agreement** — all 11,192 real messages carry a nonzero count.

**How to apply:** before trusting a decode path, ask what input would distinguish it from the wrong
version, then check whether the corpus contains that input. If not, the corpus is not evidence and the
reference's encoder is. Related: [[fixtures-are-the-weak-point]]#differential-beats-fixtures,
[[research-before-code]].
