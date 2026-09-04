---
name: the-half-you-have-may-be-the-wrong-half
description: Half a mechanism can be worse than none; ask which half is load-bearing for the other.
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-04T04:19:08.927Z
---

**When a mechanism spans several engine sites, implementing some of them can be WORSE than
implementing none.** Ask which half depends on the other having run.

`BONE_FIXED_ALIGNMENT` is three sites and one mechanism (B308, 2026-09-03): align a decoded rotation
once against the bone's `qAlignment` (`bone_setup.cpp:470`), then use the `NoAlign` variants in both
blends (`:1492`, `:1608`) because the choice is already settled. We had the `NoAlign` slerp and
neither of the others — so nothing aligned anywhere, and an antipodal pair blends the LONG way
round. Implementing the slerp alone was the defect.

**The tell is a `NoAlign`, a `Fast`, a `Unchecked` or a `Raw` variant.** Those names mean "the
precondition was established elsewhere". Reaching for one without finding where is the mistake.

**Two structural reasons this project keeps meeting it**, both worth checking directly:

- **A field in a struct GAP is never missed.** `qAlignment` sits between `poseToBone` and `flags`,
  96 + 48 = 144; every field on either side read correctly, so nothing failed.
- **A flag no content sets does NOT make the branch untestable**, and believing it did was this
  session's own wrong turn. `bone-flags` measured 0 of 924 bones across 37 models, and that was
  written up as an unclosable gap. It is a fact about ONE input. The flag is the variable under
  test, so setting it on a real model is the manipulation; what actually had to be found was the
  OTHER input — a track using the encoding the branch needs. **A zero denominator on one input is
  not a zero on the experiment.**

**Select the input by the condition the engine branches on.** Two hand-picked animation numbers
failed before that; `CalcBoneQuaternion` returns early for the raw encodings, so only a track
carrying `STUDIO_ANIM_ANIMROT` reaches the alignment — and `StudioAnimation.Tracks` already reported
each track's flags, so the instrument existed
([[look-for-the-instrument-before-building-one]]).

**Ask a sabotage WHICH tests reddened, not whether the right one did.** Deleting the flag gate here
reddened nothing: both tests set the flag on their own subject, so the gate was always true for
everything they exercised, and each stated its expectation relative to a reading that ran through
the same code. `Failed: 0` presents that as success.

Same shape as [[half-a-mechanism-is-not-parity]] and [[every-densifying-step-needs-the-delta-flag]].
Related: [[an-empty-search-needs-a-control]], [[instrument-bugs-outnumber-decoder-bugs]].
