---
name: one-run-at-a-time
description: "Never script repeated runs of a viewer or UI test; run one, read it, then decide on the next"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 7256e9d8-efff-49d6-8602-f5a3d8ea7240
  modified: 2026-09-23T06:31:45.350Z
---

Run a viewer or UI test once, read its result, then decide on the next run. Never put runs in a loop or script more than one.

**Why:** The owner, 2026-09-23, after stopping a three-run loop of the playback UI test: "you do not script 3 runs, you never scripts more than 1". Each run takes the desktop, and he watches it. A loop kept taking the machine while its own first run had already shown the test was broken, and a later file read left over from an earlier loop was nearly taken for a fresh result.

His reasoning, 2026-09-23: *"if theres an issue in the first run, you make it so you have to run that same problem 3 times, when running one at a time means you fix and dont waste time"*.

**How to apply:** For a flaky or intermittent case, run once, read the output file, and only then start the next single run. Read results from the run just made, never from a file an older run may have left. Related: [[take-the-desktop-lock-dont-defer]], [[a-measure-needs-focus]].
