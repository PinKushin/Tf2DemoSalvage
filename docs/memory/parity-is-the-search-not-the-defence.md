---
name: parity-is-the-search-not-the-defence
description: The owner's "check for parity" means SEARCH Valve's code for the mechanism, not justify what we built; and a rule written in our own comments is not an enforced rule.
metadata:
  type: feedback
---

**The owner, repeatedly, through an entire evening of me measuring the wrong things:** *"remember to
be checking for divergence from valve"* … *"you should be able to check our code for places we are
not following valve and find the problem"* … and when it landed: *"SEEEEEEE PARITY FIXES
EVERYTHING!!!"*

**Why:** every bug that night was a divergence with a citation sitting in the SDK, and in two cases
the citation was already sitting in this repository's own comments:

- `WorldRenderer.DrawModel` said *"a SKINNED model is put there by its bones and its matrix stays at
  identity"*. Nothing enforced it. It held by accident — a player's pose is (0,0,0), a merged item's
  is (0,0,0) by construction — until a skinned prop with a real networked origin arrived and was
  placed twice, ten thousand units off the map.
- `EntityModels.Absolute` implemented `CalcAbsolutePosition`'s bone-merge branch and applied it to
  everything, so a parented prop lost its own angles. That is the 90° gate.

**How to apply:** when a symptom is visual and the code "looks right", go and read the engine
function that owns the whole mechanism, then check each of its branches against ours by name.
`CalcAbsolutePosition` has THREE and we had two. `ShouldDraw` has a render-mode test we did not.
`GetSkin` has a bodygroup twin in `ValidateModelIndex`. Every one of those was found by reading
Valve rather than by measuring our output — see [[read-the-spec-before-measuring-our-data]] and
[[a-bug-is-a-divergence-search-first]].

**And a rule in a comment is not a rule.** If a comment states an invariant — "X stays at identity",
"only Y sets this" — either an assertion enforces it or it is a description of today's accident.
Both of the above were true when written and false a month later, with nothing red.

**The corollary that cost the most hours: check the instrument against Valve too.** A log line
reading `door_grate003_top at (0, 0, 0)` was the ILLUMINATION point, not the position, and five
conclusions were drawn from it. See [[instrument-bugs-outnumber-decoder-bugs]] and
[[a-picture-is-assertable]].
