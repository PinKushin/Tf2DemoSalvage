---
name: the-view-origin-is-the-camera-not-a-player
description: CurrentViewOrigin() is whatever camera draws the frame; gating it on first person silently disables every distance measurement.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T03:55:38.680Z
---

`MomentInfo.EyeCamera` was filled only in first person, so free/chase camera left `ViewOrigin` null —
silently disabling distance-based effects: the areaportal window's distance blend fell back to the
brush's renderamt, a solid `TOOLSBLACK` panel in every spawn window (B358), and
`UTIL_ComputeEntityFade` drew every entity at full alpha (B365).

**Why:** owner, before it was measured — *"this is a stv demo, pvs should update based on the camera,
not a player themselves... that is basically guaranteed to be how valve does it."* Correct —
`CurrentViewOrigin()` is the render view's origin whatever drives it; the engine has no first-person
branch, since a demo viewer is a spectator.

**How to apply:** the view origin comes from the DEVICE that built the frustum, not from a player, and
not from a second reading of a camera. When something measuring distance from the viewer looks right
in first person, check it in free look before believing it. The test shape that missed this for a
month: every unit test set the origin by hand, testing arithmetic below the defect and never how a
frame supplies it.

See [[instrument-bugs-outnumber-decoder-bugs]], [[output-level-assertion-or-it-is-not-done]],
[[a-dropped-field-falls-to-a-computed-default]].
