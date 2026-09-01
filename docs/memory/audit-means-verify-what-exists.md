---
name: audit-means-verify-what-exists
description: "A parity audit checks whether what we DREW is right, not which engine functions we never implemented; branch count ranks the wrong axis."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-01T14:35:38.853Z
---

The owner, 2026-09-01, correcting an audit that had been ranking engine functions by branch count
and filing the unimplemented ones: *"i wanted you to make sure everything we have implemented is
right, had valve partity, and is not buggy. Theres far more useful thinks than ragdolls still
available, like interp, and out fps being way way too low."*

**Why:** an unimplemented mechanism is VISIBLE — it is absent, somebody notices, it can be filed any
time. A wrong implementation is INVISIBLE: it draws something, the suite is green, it looks
finished. Only the second class needs an audit to find it, and branch-counting cannot rank it,
because a function implemented well and one implemented badly have the same branch count.

The concrete cost: the audit's top-ranked, most-measured finding was 299 undrawn corpses — and the
owner runs a comp config with ragdolls off, so players vanish on death in his real game. The best
measurement of the session was aimed at a feature he would switch off if it existed.

**How to apply:** rank by what we ALREADY DRAW, and ask of each whether it matches the engine on
every branch and uses the value the engine would use. `InterpolationDelayTicks = 7` hardcoded beside
a declared-but-unread `cl_interp` is the shape to look for — right for stock defaults, wrong for the
config he runs. Performance counts as correctness here too: TF2 plays these demos at 600+ fps, and a
gap that large is a defect in code we wrote, not a budget.

Does not retract the ragdoll findings — see [[a-gap-can-be-filed-backwards]]. They are correct and
recorded; they are simply not the priority they were written up as. Related:
[[valve-parity-is-the-first-principle]], [[decoding-a-field-is-not-honouring-it]],
[[not-every-setting-needs-a-bind]].
