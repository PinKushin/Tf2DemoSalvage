---
name: compare-with-the-same-camera
description: A TF2-vs-viewer sound comparison needs the same camera on both sides; the 1024 impact gate is measured from MainViewOrigin
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-23T20:49:42.035Z
---

When comparing sounds against TF2, put both on the SAME camera. TF2's impact sounds are gated at 1024 units from
`MainViewOrigin()` (`tf_fx_impacts.cpp:75`). So a different camera gives a different set of sounds, and the difference
looks like a bullet bug.

**Why:** I ran our viewer with `--first-person` on gummo while TF2 sat on the STV default camera. I then chased
"missing" and "extra" impacts that were only the camera. I also read TF2's `getpos` zeros as its camera. The owner:
*"its an stv demo, of course it doesnt, wtf you doing"*. An STV demo has no player position for `getpos` to print.

**How to apply:** use TF2's default STV camera against our default run, or spectate the same player in both. Settle
the camera before reading any difference. See [[sourcetv-has-a-local-player]] and [[check-at-the-owners-moment]].
