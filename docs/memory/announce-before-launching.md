---
name: announce-before-launching
description: "Say a desktop-taking run (UI suite, playback check, viewer) is starting BEFORE firing it, not after the tool returns."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-26T18:48:51.191Z
---

Announce a desktop-taking run (gate phase 2/UI suite, phase 3/playback check, any viewer launch) in
text BEFORE the call that starts it. Never report it as "running" after the call already returned.

**Why:** 2026-09-26 the owner watched 37 viewers crash on screen before any message said gate 2 had
started, and mistook it for gate 1. The foreground call ran two minutes before returning; "running
now" arrived after the fact. Owner: *"you dont say you a doing something after its done"*.

**How to apply:** one line before the call ("Starting gate phase 2 — viewers will open"). Also: only
phase 2 uses the real application; phase 1 is unit/integration only; phase 3 will fold into the UI
suite. See [[take-the-desktop-lock-dont-defer]].
