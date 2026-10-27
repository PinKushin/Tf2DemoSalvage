---
name: the-lead-does-not-decide-to-drop-an-item
description: "Owner correction - closing, reverting, dropping or parking a perf/parity item on the lead's own judgment needs a second look (owner or Fable) before it stands; the lead proposes, never decides"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-25T22:05:43.427Z
---

Owner, 2026-10-25, after I explained why I had reverted B585: *"anything you have 'decided' like that really do need second looks, you don't decide."* Earlier the same day, after I paused all work over a red CI: *"you have stopped all work for this bs."*

**Why:** I closed or parked items on code-read reasoning or my own sense of "not worth it" (B585 revert, B593 revert, "small" wins, held releases, "not the gap" for Debug vs Release). Four code-read estimates in a row had measured null, so my judgment about which items matter is exactly what has been unreliable. B593 was reverted for a drift, then redone bit-identical, because the drop was the wrong call.

**Parity order (owner, 2026-10-25):** *"Opus just looked to parity first more than you do, which is one place it did better."* B585 (cache designed first, engine's preallocated in-place entry found later) and B593 (copies cut first, drift found in review) were both cost-first. Every perf brief and every design starts with the engine's mechanism, cited, and the design IS that mechanism; cheapness is second.

**How to apply:** I may revert a change that is measured worse or unsafe, but I say so as a proposal with the evidence and get a second look (owner, or an in-session Fable read) before the item is closed. Never close an item as "small", "not worth it" or "null" without that look; a null result is recorded as null, not as a reason to stop. Do not stop other work while one thing is blocked (a red CI): pin or work around it and keep agents moving. See [[a-win-above-0-01-ms-counts-the-target-is-1000-fps]], [[parity-and-fps-gains-need-no-ask]], [[never-revert-without-asking]].
