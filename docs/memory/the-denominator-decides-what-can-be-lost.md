---
name: the-denominator-decides-what-can-be-lost
description: "A coverage test can only find things missing from the set it enumerates; pick the denominator from what must be produced, never from the mechanism producing it."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-28T04:54:48.950Z
---

A test that asks "was everything covered?" is only as good as the set it walks. Walk the
**mechanism's** input and anything the mechanism cannot see is outside the question — the test passes
while the output has a hole in it.

Measured 2026-08-28. A world cull drew surfaces from the BSP leaves it could see, and the coverage
test asked: for each face **named by a visible leaf**, is it drawn? Displacements are named by no
leaf at all — `vbsp` builds a leaf's face list from its portals and detail faces, and a displacement
is neither — so the ground could vanish entirely with the test green. The owner found it by looking
at the screen.

The test even had a control, `checkedFaces > 0`, and it passed on twelve thousand brush faces while
sixty terrain faces went uncounted. **A control on the total does not control for a missing
category.**

**Why:** the correct denominator is what must be PRODUCED, not what the producer consulted. Here that
is every surface the uncalled renderer draws; each one dropped must then be justified — outside the
frustum, or excluded by a filter that can be checked independently. Stated that way the test cannot
be satisfied vacuously.

**How to apply:** when writing a coverage or completeness test, ask what set the implementation
iterates and deliberately choose a different one. Then add a control for each CATEGORY the output can
contain, not just for the total — and verify the control fires: the first corrected version of this
test still passed with the camera indoors, where every displacement was legitimately off screen, so
"at least one orphan was drawn" had to become its own assertion. See
[[an-empty-search-needs-a-control]] and [[instrument-bugs-outnumber-decoder-bugs]].
