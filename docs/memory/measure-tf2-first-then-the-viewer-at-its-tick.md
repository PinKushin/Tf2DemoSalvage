---
name: measure-tf2-first-then-the-viewer-at-its-tick
description: "A TF2-vs-viewer comparison measures live TF2 FIRST, reads the exact tick and subject it landed on, then measures the viewer at that tick and subject."
metadata:
  node_type: memory
  type: feedback
  originSessionId: a7745234-ed21-42a5-a0c8-7c85003d2f62
  modified: 2026-11-07T07:40:00.879Z
---

A performance or picture comparison with live TF2 runs TF2 first, reads where it actually is (`demoui`'s tick, who
the camera follows), and only then measures this viewer at that same tick and subject (`--tick`, `--spectate`).

**Why:** the owner, 2026-11-07, after I compared TF2 at ticks 6553/6923 on Beleleu against a viewer run at
`--tick 5000 --first-person`: *"thats why you are always suppose to go TF2 first, our viewer second, on measurements.
You always need to know the actual exact tick you got in real tf2 and its not predictible or forceable really"*.
`demo_gototick` lands within a few ticks and a paused demo stops answering RCON, so TF2's position cannot be chosen,
only read.

**How to apply:** never quote a TF2-vs-viewer ratio from runs at different ticks, players or views. Read the tick and
subject off TF2's own screenshot, then pass both to the viewer. See [[driving-tf2-demo-playback]],
[[compare-with-the-same-camera]].
