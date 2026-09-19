---
name: my-own-processes-are-mine
description: "A viewer or dotnet process found running is almost always one my own background/timeout commands left; check my launches before suggesting it is the owner's."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-18T21:59:31.695Z
---

**2026-09-18.** A build failed because `tf2demoview` PID 17756 held a DLL, and I told the owner it "may be yours". It was mine: a
`timeout`-wrapped `run-exclusive` launch whose `taskkill` ran after the build had already started. The owner: *"that wasnt mine
either, that was yours, you need to pay attention"*. Earlier the same day I had also claimed a run was "clean" that was only a
paused `--shot` frame.

**Why:** attributing my own leftovers to the owner wastes his attention and reads as not tracking what I launched.

**How to apply:** before building the viewer or blaming a lock on someone else, list my own background tasks and kill my own
leftover viewer first (`taskkill //IM tf2demoview.exe //F` only for processes I started). Never overlap a build with a viewer
run I launched. Related: [[take-the-desktop-lock-dont-defer]], [[a-gui-exe-does-not-hold-the-lock]].
