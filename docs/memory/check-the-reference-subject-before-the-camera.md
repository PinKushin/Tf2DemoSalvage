---
name: check-the-reference-subject-before-the-camera
description: Before blaming a camera for a TF2 mismatch, probe whether the spectated player is alive and holds the weapon in the reference; a dead target is chased (B522)
metadata:
  node_type: memory
  type: feedback
  modified: 2026-10-11
---

A TF2 reference "spectating nezay" did not match `--spectate nezay`, and the camera was not at fault: nezay was
dead at that tick, and TF2's pictures showed another player (gummo) under a label reading NEZAY. Shot as
`--spectate gummo` the viewer matched both of TF2's pictures.

**Why:** the label on a reference is not its subject. `spec_player` on a dead, observing player is refused by
`C_HLTVCamera::SpecPlayerByPredicate`, so TF2 stays on the previous target; and a dead target is chased rather than
shown in-eye (`CalcInEyeCamView`'s first branch), which the viewer already does.

**How to apply:** three probes before any camera code — `moment-dump <demo> <tick> <entity>` (life state, observer
mode and target), `viewmodels <demo> <entity>` (does the weapon match the picture), `round-timer <demo>` (which ticks
a timer reading covers). A round timer names a tick only within a round: it is reset many times a match, so the
same reading recurs. Ask for a reference of a LIVING player with the tick confirmed.
