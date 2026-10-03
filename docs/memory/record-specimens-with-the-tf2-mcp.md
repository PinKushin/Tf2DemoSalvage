---
name: record-specimens-with-the-tf2-mcp
description: "When a new TF2 build needs a specimen demo, record it yourself through the tf2 MCP instead of asking the owner."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-03T01:45:30.915Z
---

2026-10-02, after the Halloween update: the assistant asked the owner to record a demo on the new build. Owner:
*"then you can just use the fucking mcp to make a demo"*.

**How to apply:** the `mcp__tf2__*` tools (launch, console, wait_for, screenshot, quit) drive the owner's TF2. To get a
specimen on a new build: launch, load a map locally, `record <name>`, play/wait briefly, `stop`, quit — then census it.
Never ask the owner to record. TF2 takes the desktop, so it goes through the machine-wide lock like the viewer.
Related: [[driving-tf2-demo-playback]], [[take-the-desktop-lock-dont-defer]].
