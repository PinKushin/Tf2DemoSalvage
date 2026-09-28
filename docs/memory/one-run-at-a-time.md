---
name: one-run-at-a-time
description: "Never script repeated runs of a viewer or UI test; run one, read it, then decide on the next"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 7256e9d8-efff-49d6-8602-f5a3d8ea7240
  modified: 2026-09-23T06:31:45.350Z
---

Run a viewer or UI test once, read the result, then decide the next run — never loop or script more
than one.

**Why:** owner, after stopping a three-run loop: *"you do not script 3 runs, you never scripts more
than 1... if theres an issue in the first run, you make it so you have to run that same problem 3
times, when running one at a time means you fix and dont waste time."* Each run takes the desktop and
he watches it.

**How to apply:** for a flaky case, run once, read the output file just produced (never one an older
run left behind), then start the next single run. Related: [[take-the-desktop-lock-dont-defer]],
[[a-measure-needs-focus]].
