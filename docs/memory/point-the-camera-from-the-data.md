---
name: point-the-camera-from-the-data
description: "Eight guessed cameras landed inside walls and under terrain; the ninth, aimed from the lump's own coordinates, settled it."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T03:55:10.479Z
---

Verifying a rendering change by screenshot fails when the camera is guessed. Eight captures landed
inside a barn, under terrain, behind a wall, on a rock face — each costing a ~30s viewer boot —
before a probe was taught to print the densest cell of the relevant data plus sample origins. The next
capture was decisive.

**Why:** a map's layout isn't recoverable from a picture of the wrong part of it; a black frame is
indistinguishable from the feature being absent. Guessing silently converts "the feature doesn't draw"
into "I haven't seen it yet".

**How to apply:** before capturing to verify geometry, make the probe reading the data print WHERE it
is — bounding box, densest cell, sample origins with angles. Set the camera from that. Also: the
viewer takes 20-30s to boot, so `ls` right after launch reports the file missing — wait on the file,
never a listing. Related: [[take-your-own-screenshot]], [[instrument-bugs-outnumber-decoder-bugs]],
[[a-picture-is-assertable]].
