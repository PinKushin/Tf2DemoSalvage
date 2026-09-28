---
name: a-reference-count-can-depend-on-fps
description: "TF2 merges per-frame lists (physics impact sounds); a TF2 capture's count reflects ITS frame rate, so compare at the same +fps_max"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-24T03:21:06.094Z
---

Some Valve output is per client FRAME, not per tick: `PlayImpactSounds` plays one list that
`AddImpactSound` merged across every tick the frame's `Simulate(frametime)` crossed. A TF2 reference
capture carries the frame rate it ran at — f12's corpse impacts sat on a clean 4-tick rhythm (~16fps,
backgrounded while captured).

**Why:** 2026-09-23, ours at ~290fps played 27 impacts to TF2's 11, looking like a physics bug; at
`+fps_max 16` ours played 15 on the same cadence.

**How to apply:** when an event count differs from TF2, check the reference's spacing first — a
regular N-tick rhythm means per-frame batching; rerun at the matching `+fps_max` before debugging the
producer. See `docs/findings/63` and [[compare-with-the-same-camera]].
