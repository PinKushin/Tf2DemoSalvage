---
name: an-inverted-flag-is-not-a-disabled-flag
description: A sabotage that inverts a condition tests a different claim than one that disables it — check which experiment a delegated sabotage actually ran.
metadata:
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-02T00:00:00.000Z
---

**Read which sabotage a subagent actually performed, not which one it was asked for.** Two edits to
the same condition can prove opposite things, and the report reads identically.

B269. The instruction was: make the loop-aware blend *always take the plain branch*, so that
`At_ALoopingPoseParameterAcrossTheWrap_TakesTheShortWay` — the test the whole loop-flag seam exists
for — is shown to be failable. What the agent did was **invert** the condition, so looping and
non-looping swapped. That reddened `At_ANonLoopingPoseParameterAcrossTheSameGap_Interpolates`, the
CONTROL, and left the looping case still passing. It reported "sensitive" and it was not wrong about
what it measured; it measured something else.

**Why the distinction is not pedantic.** Disabling asks "does anything depend on this being on?".
Inverting asks "does anything depend on this being right way round?". A test that only reads the
flag's *presence* survives inversion; a test that reads its *effect* survives neither. Only the
first question tells you whether the feature is load-bearing.

**Two things followed, and both are the practice now:**

- **Say what the sabotaged code must DO, not which line to touch.** "Force the plain branch" is a
  specification; "change the condition on line 1692" invites any edit to that line.
- **The analyzers can block the obvious sabotage, and that is a hint to move up a level.** Replacing
  the condition with `false` here tripped CA1822/S2325 — the method no longer touched instance data
  and had to be static. Widening `LoopingLerp`'s own `>= 0.5f` threshold to `>= 2f` was the clean
  inverse edit: it makes the wrap unreachable without changing any signature, and it reddened the
  right test plus three animation-cycle tests that share the helper.

See [[instrument-bugs-outnumber-decoder-bugs]] for the family this belongs to, and
[[one-subagent-and-prefer-cheap-models]] for what a cheap model is fine to be trusted with —
sabotage still qualifies, provided the result is read rather than accepted.

## A sabotage also tells you what a test was actually measuring

Same session, B273. Two corpus tests were written to cover the applied-time stamping and both
looked right. Severing the stamping — dropping the lag from `track.Add` — left **both green**: they
asserted on the lag HISTOGRAM, which is measured beside the stamping rather than through it.

Nothing about reading those tests suggests that. They name the right subject, use real demos, and
would have been believed. The sabotage is what separated "covers the change" from "mentions the
change", and the fix was a third test reading the number the interpolation actually used out of the
track — which reddens.

So run the sabotage even when the tests are yours and you are confident. The question it answers is
not "did I write a test" but "does anything fail when the feature stops working".
