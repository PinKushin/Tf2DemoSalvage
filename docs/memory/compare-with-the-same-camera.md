---
name: compare-with-the-same-camera
description: A TF2-vs-viewer sound comparison needs the same camera on both sides; the 1024 impact gate is measured from MainViewOrigin
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-23T20:49:42.035Z
---

When comparing sounds against TF2, use the SAME camera on both sides — TF2 gates impact sounds at
1024 units from `MainViewOrigin()` (`tf_fx_impacts.cpp:75`), so a different camera changes which
sounds play and looks like a bullet bug.

**Why:** ran our viewer `--first-person` on gummo while TF2 sat on the STV default camera, then
chased "missing"/"extra" impacts that were only the camera; also misread TF2's `getpos` zeros as its
camera position. Owner: *"its an stv demo, of course it doesnt, wtf you doing"* — an STV demo has no
player position for `getpos`.

**How to apply:** use TF2's default STV camera against our default run, or spectate the same player
in both. Settle the camera before reading any difference. See [[sourcetv-has-a-local-player]],
[[check-at-the-owners-moment]].
