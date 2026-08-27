---
name: linq-is-a-test-tool
description: LINQ never on a hot path; off one it is allowed when what it buys outweighs the cost. Tests use it freely.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-27T20:56:19.864Z
---

**Never LINQ on a hot path. Off one, it is allowed when what it buys outweighs the cost — and it is
always a cost.** Tests may use it freely. Recorded as D107.

The owner, 2026-08-27, on a `string.Join` over a `Select` in the trace writer: *"linq can be slow if
its in a hot path, i dont like link in the program proper so performance stays high, its really only
a test thing in this project."* Then, correcting my first write-up which had made it an outright
ban: *"if its not on a hot path and the better things linq does overrides the downsides, i am open to
having it in the program, but it is a performance hit."*

**The overstatement was mine, and worth remembering as its own lesson.** I wrote the rule stricter
than its author intended AND argued in the entry against the two-standard approach he actually
holds — a rule written down more absolutely than it was given gets cited later as if it were. See
[[name-the-reading-you-picked]].

**How to apply**, as a judgement rather than a keyword ban:

1. Hot path? No, whatever the query buys.
2. Otherwise, does it genuinely read better or fail less often? If yes, take it knowing the cost. A
   wash goes to the loop.

**Hot here means** per-message decode and text, per-entity instancing and posing, per-batch drawing —
where B181, B189 and B191 all landed. Map load, asset resolution, config parsing and one-shot
reporting are not.

**The debt is unmeasured in the way that matters.** 45 of 356 files in `managed/` declare
`using System.Linq;`, but the useful number is how many do it on a hot path, and nobody has counted
that. Do not convert cold queries for tidiness — this project prefers
[[measure-the-output-not-the-capability]].
