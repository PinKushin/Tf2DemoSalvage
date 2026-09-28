---
name: check-at-the-owners-moment
description: "A fix for something the owner saw is checked at HIS demo and HIS moment, not the first place the symptom shows up; \"did you actually look at the right ticks?\""
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-11T14:13:06.438Z
---

**Reproduce and re-check at the owner's own moment — his demo, his kind of moment, his camera** — not
the first tick where something wrong turns up.

B380 (2026-09-11): he reported the 2008 sticky launcher filling the screen on the SourceTV demo,
during what he thought was reload or charge. The fix was verified on the POV demo at the first sticky
deploy (a different wrong frame) and written up "FIXED". He asked: *"did you actually look at the
right ticks?"* — no. His moment was IDLE, playing today's `ref` pose; the first captures never
reached idle.

**Why:** a symptom that looks alike can come from a different sequence, tick, or branch.

**How to apply:** before any capture, write down which demo/player/action he named; find those ticks
with an instrument that can see that action (here, `viewmodels` keyed on sequence and restart, not
just model). State which moments were checked when reporting. Related:
[[state-the-assumptions-the-owner-can-falsify]], [[instrument-bugs-outnumber-decoder-bugs]].
