---
name: a-win-above-0-01-ms-counts-the-target-is-1000-fps
description: "Owner's frame-time yardstick - only wins under 0.01 ms are \"small\"; the long-run target is 1000 fps (a 1 ms frame) for everything; report ms AND percent of frame, never call 0.03-0.1 ms small"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-25T21:37:02.688Z
---

Owner, 2026-10-25, after I called a 0.03 ms saving small: *"I think you also think 0.03 ms is small, but that's actually kind of medium when we are talking single frame times, and those frames should and can be done 300+ times every second"*, then *"Really I would only call sub 0.01 ms wins small, that is the real target for everything, so we can run 1000 fps."*

**Why:** the frame is made of many items of this size (drawlist 0.22 ms, filters 0.06 ms); at a 3.3 ms frame 0.03 ms is ~0.9% (~+2.7 fps), at 1 ms it is 3%. TF2 itself runs 0.81-1.12 ms a frame in an empty room (893-1,236 fps, measure skill), so 1000 fps is TF2's own level, not an invention.

**How to apply:** judge a change by cost and risk (cheap, bit-identical to the engine, tested, recorded), not by size. Keep any win >= 0.01 ms that meets that. In reports give the ms and the percent of the frame, not the word "small". A parity drift is never worth a saving of this size - fix the drift, do not drop the win (B593 was reverted for a drift, then redone). Related: [[parity-and-fps-gains-need-no-ask]], [[measure-the-output-not-the-capability]].
