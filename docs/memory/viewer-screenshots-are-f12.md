---
name: viewer-screenshots-are-f12
description: The owner sends screenshots by pressing F12 in the viewer; they land in %LOCALAPPDATA%\Tf2DemoSalvage\shot-<stamp>.png next to viewer.log.
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-14T19:28:28.756Z
---

**When the owner says "there's a screenshot for you", they pressed F12 in the viewer.** The
captures are written to

```
C:\Users\pinku\AppData\Local\Tf2DemoSalvage\shot-yyyyMMdd-HHmmss.png
```

— the same folder as `viewer.log`, since the path is built from `ViewerLog.Path`'s directory
(`MainForm.ProcessCmdKey`, `Keys.F12`). Read the newest by timestamp:

```bash
ls -t "C:/Users/pinku/AppData/Local/Tf2DemoSalvage/"shot-*.png | head -3
```

**Why it matters:** this was set up in-session and then forgotten, and the owner had to say so
twice. Not knowing where the screenshots are wastes the one instrument that can answer a UI
question — and the standing rule here is that anything about the interface which cannot be checked
by LOOKING is a question for the owner rather than a claim.

The picture comes from the live renderer rather than the offscreen target, deliberately: the
parallel path drifted once already (decals were added to one and not the other) and its pictures
were still being read as though they showed the viewer.
