---
name: unreachable-can-be-proved-not-just-observed
description: An uncovered branch is a finding either way — write the input that reaches it, or prove by arithmetic that nothing can, and record the proof beside the code.
metadata:
  type: project
---

Closing the last of this repository's reachable coverage (2026-08-19) split every gap into exactly
two kinds, and treating them the same is what leaves both unresolved.

**Kind one: nothing has written the input yet.** Most gaps. They look unreachable because a demo
cannot produce them — a stated count a body cannot support, an assembly cut mid-block, a property
definition no schema emits. The right answer is to build the input, and the fact that a recording
cannot is the reason the branch matters: it is what decides whether a wrong file gets diagnosed or
silently mis-decoded.

**Kind two: the branch is genuinely dead, and it can be shown.** `LoopingCurve`'s re-check has an
`else` arm that cannot run. It is reached only after `p1` has been raised into `[1, 2)`, and every
path there leaves `p0` below `p1`: either `p0` was untouched and is under 1, or the first pass
raised it, which happens only when `p0 < p1` and raising both preserves the order. A third case
would need the first pass to have raised `p1`, but then `p1 >= 1` and the `p1 < p2` test guarding
the block cannot hold against a `p2` in `[0, 1)`. That is a proof, not an observation, and it does
not go stale the way "no demo does this" does.

**Why:** the two kinds are indistinguishable in a coverage report and demand opposite work. Chasing
kind two writes contorted tests that never pass; dismissing kind one as "unreachable" is how a
guard ships untested. See [[an-uncoverable-gap-is-usually-your-reader]] — the default assumption
should still be kind one.

**How to apply:** for a gap that resists, do the arithmetic on what can reach it. If it is dead,
**keep the code** when it is a transcription (Valve's own `LoopingLerp_Hermite` has the same arm,
and deleting it makes the two harder to compare) and put the reasoning in the remarks beside it, so
the gap reads as a recorded conclusion rather than an oversight. If it is not dead, the input is
writable — see [[author-the-specimen-the-corpus-lacks]].

Two other things this pass established, both worth reusing:

- **State the property over every case at once when the cases share a code path.** "No registered
  user-message name decodes a 4096-bit body" is one test covering forty layouts, and it covers the
  forty-first the day it is added. It found a real defect that forty per-message tests would each
  have passed.
- **Every refusal test needs a sensitivity control in the same file.** Assertions that something
  did NOT happen are all satisfied by a method that fails unconditionally, and a decoder that
  refused everything would look identical.
