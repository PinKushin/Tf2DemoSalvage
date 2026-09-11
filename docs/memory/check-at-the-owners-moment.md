---
name: check-at-the-owners-moment
description: "A fix for something the owner saw is checked at HIS demo and HIS moment, not the first place the symptom shows up; \"did you actually look at the right ticks?\""
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-11T14:13:06.438Z
---

**Reproduce and re-check at the owner's own moment: his demo, his kind of moment, his camera.** Not the first tick
where something wrong turns up.

B380 (2026-09-11): he reported the 2008 sticky launcher filling the screen on the SOURCETV demo, during
what he thought was a reload or a charge. The fix was verified on the POV demo at the first sticky
deploy, where a different wrong frame happened to show, and was written up as "FIXED". He asked *"did
you actually look at the right ticks?"* They had not been. His moment turned out to be IDLE, which
played today's `ref` pose; the first captures never reached idle at all.

**Why:** a symptom that shares a look can come from a different sequence, tick or branch. A fix judged
at the wrong moment can be real and still not be the fix for his report.

**How to apply:** before any capture, write down which demo, which player, and which action he named.
Then find those ticks with an instrument that can see that action. Here that meant
`viewmodels` keyed on sequence and restart, not only model and item. Capture there, and say which moments
were checked when reporting. Related: [[state-the-assumptions-the-owner-can-falsify]],
[[instrument-bugs-outnumber-decoder-bugs]].
