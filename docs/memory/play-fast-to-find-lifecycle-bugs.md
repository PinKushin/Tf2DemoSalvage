---
name: play-fast-to-find-lifecycle-bugs
description: A playback test at 8x on a real match finds create/remove bugs that 1x and era specimens never reach
metadata:
  node_type: memory
  type: feedback
  originSessionId: 7256e9d8-efff-49d6-8602-f5a3d8ea7240
  modified: 2026-09-23T05:52:37.401Z
---

A test that plays a demo should play a REAL match (z1800, never an era specimen) at fast-forward. At 8x, twenty seconds of test covers 160 seconds of match, so many more corpses, gibs and effects are created and removed. That churn found B418, a removed corpse's mindist firing on a core with no unit, which every 1x run and the f12 playback check had missed.

**Why:** The owner, 2026-09-23, on `Transport_PlayAtEightTimes_AdvancesThroughTwentySecondsOfPlayback`: "this is a really good test, it needed the fast forward to be found." He also rejected the granary era specimen for it: "its a era specimin, z1800 is a real demo".

**How to apply:** Run a lifecycle or hang check in the shared UI session (one session, never a second viewer process), on z1800, at 8x. Take the first reading only once playback is seen moving, and fail a pass that beats the wall clock, because a seek can imitate playback. Related: [[output-level-assertion-or-it-is-not-done]], [[instrument-bugs-outnumber-decoder-bugs]].
