---
name: read-the-encoder-not-the-decoder
description: A reference parser's encoder states intent its decoder only implies; corpus silence about a case is not evidence the case is absent.
metadata:
  type: project
---

When cross-checking a format against a reference implementation, read its **encoder**,
not only its decoder. The decoder can be read the same wrong way you read the spec —
both are one person's interpretation of the wire. The encoder states intent: it has to
choose what to emit, so a special case appears there as a deliberate branch.

That is how `svc_TempEntities`' count byte was settled on 2026-08-10. demostf/parser's
encode side has `(1, Some(event)) if event.reliable => 0` — a count of **0** means one
effect sent reliably, not an empty message. This project's decoder looped `count` times,
so it produced nothing and silently left the body unread.

**The corpus could not have caught it, and its silence looked like agreement.** All
11,192 `svc_TempEntities` messages across protocols 11–24 carry a nonzero count. Same
turn, the fire delay looked like it should be sign-extended by analogy with every other
signed field here; it is a plain `u8`, and all 55,441 temp entities in the corpus have
delay 0.00, so signed and unsigned predict the same observation everywhere. Two
questions, one answerable only off the reference.

**How to apply:** before trusting a decode path, ask what input would distinguish it
from the wrong version, then check whether the corpus contains that input — a count of
zero, a negative coordinate, a non-ASCII name. If it does not, the corpus is not
evidence and the reference's encoder is. Related: [[differential-beats-fixtures]],
[[fixtures-are-the-weak-point]], [[research-before-code]].
