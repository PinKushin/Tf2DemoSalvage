---
name: my-own-processes-are-mine
description: "A viewer or dotnet process found running is almost always one my own background/timeout commands left; check my launches before suggesting it is the owner's."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-18T21:59:31.695Z
---

A build failed because `tf2demoview` held a DLL, and it was blamed as possibly the owner's — it was
mine, a `timeout`-wrapped launch whose `taskkill` ran after the build had already started. Owner:
*"that wasnt mine either, that was yours, you need to pay attention."* Earlier the same day a paused
`--shot` frame was also misreported as "clean".

**Why:** attributing my own leftovers to the owner wastes his attention.

**How to apply:** before building the viewer or blaming a lock on someone else, list and kill my own
background tasks first. Never overlap a build with a viewer run I launched. Related:
[[take-the-desktop-lock-dont-defer]], [[a-gui-exe-does-not-hold-the-lock]].
