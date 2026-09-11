---
name: point-the-camera-from-the-data
description: "Eight guessed cameras landed inside walls and under terrain; the ninth, aimed from the lump's own coordinates, settled it."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T03:55:10.479Z
---

Verifying a rendering change by screenshot fails when the camera is guessed. Eight captures on
`koth_harvest_final` landed inside a barn, under the terrain, behind a wall, and on a rock face —
each one costing a ~30 second viewer boot — before the `game-lumps` probe was taught to print the
densest 512-unit cell of fixed-orientation detail sprites and three sample origins. The next capture
was decisive.

**Why:** a map's layout is not recoverable from a picture of the wrong part of it, and a black or
solid-coloured frame is indistinguishable from the feature being absent. Guessing also silently
converts "the feature does not draw" into "I have not seen it yet".

**How to apply:** before taking a capture to verify geometry, make the probe that reads the data
print WHERE the data is — a bounding box, the densest cell, a few sample origins with their angles
and lighting. Then set `TF2VIEW_CAMERA` from that. Also: the viewer takes 20-30 seconds to boot and
load a map, so `ls` immediately after launching reports the file missing; wait on the file, never on
a listing. Related: [[take-your-own-screenshot]],
[[instrument-bugs-outnumber-decoder-bugs]], [[a-picture-is-assertable]].
