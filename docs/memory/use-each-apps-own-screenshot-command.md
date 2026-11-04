---
name: use-each-apps-own-screenshot-command
description: Capture TF2 and the viewer with their own screenshot commands; a desktop capture is almost never needed here.
metadata:
  node_type: memory
  type: feedback
---

Capture TF2 with its own screenshot command and the viewer with its own capture path. TF2 keeps working in the background, so neither needs the window in front.

**Why:** owner, 2026-11-04, after an agent's desktop captures barely showed the viewer: *"i dont mind capturing the desktop, but we should basically never need to in this project, because TF2 and our viewer should both have their OWN screenshot commands that dont capture the desktop at all, tf2 will literally work in the background"*. A desktop capture is not forbidden, it is the wrong instrument: it records whatever is in front.

**How to apply:** if the viewer's capture cannot reach a moment (mid-seek, for example), extend the viewer's capture rather than falling back to the desktop. Open every shot before reporting from it or showing it. Put this in agent briefs for visual work. See [[take-your-own-screenshot]].
