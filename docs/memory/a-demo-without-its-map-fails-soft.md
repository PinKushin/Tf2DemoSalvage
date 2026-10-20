---
name: a-demo-without-its-map-fails-soft
description: "A demo whose map is missing still opens and fails soft; it is possible for testing, not advertised, not a supported play mode (D224)."
metadata:
  node_type: memory
  type: feedback
---

Owner, 2026-10-19: *"lets not cut it off arbitrarily, we auto dl the maps so, a regular user should never run into it, and id rather it fail soft then fail hard in a no map available situation"*. TF2 refuses such a demo; this viewer opens it. It stays possible (the empty-view frame measurement uses it), is not advertised in the user docs or `--help`, and is not a supported play mode.

**Why:** the maps are downloaded automatically, so a user does not meet it, but a hard failure serves nobody and an empty view is a legitimate test input.
**How to apply:** never add code to refuse a map-less demo, never remove code that survives one; do not describe it as a feature. Recorded in D224. Related: [[an-addition-above-valve-is-allowed]].
