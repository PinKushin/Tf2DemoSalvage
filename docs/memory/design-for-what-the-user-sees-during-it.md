---
name: design-for-what-the-user-sees-during-it
description: "Before designing an operation for cost (memory, latency), settle what the user sees WHILE it runs; scrubbing must give live feedback."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-17T17:38:22.981Z
---

2026-10-17, D217: four pieces were planned to cut the timeline to checkpoints plus windows, with "latest wins" window builds. Then the owner noticed that this kills live scrubbing. His words: *"that design kinda needed to be here from the beggining... scub means nothing if it doesnt give youu live feedback really"*. The fix is a light layer, the demo's own stated values, kept whole and scrubbed live, plus a heavy simulated layer built by windows on release.

**Why:** the plan optimized memory and seek time and never asked what the screen shows during a drag. The owner caught it, not the plan.

**How to apply:** for any change to an interactive operation (scrub, seek, load, toggle), write down what the user sees during the operation before choosing a mechanism. Treat a design that makes it go blank or lag as wrong, whatever it saves. Related: [[ask-what-the-request-is-for]], [[valve-parity-is-the-first-principle]].
