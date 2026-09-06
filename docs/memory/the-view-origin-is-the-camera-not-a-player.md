---
name: the-view-origin-is-the-camera-not-a-player
description: CurrentViewOrigin() is whatever camera draws the frame; gating it on first person silently disables every distance measurement.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-06T14:30:42.223Z
---

`MomentInfo.EyeCamera` was filled in only when the viewer was in first person
(`_firstPerson ? FirstPersonCamera() : null`), so `MomentScene.Pose` set `ViewOrigin` to null for a
free or chase camera. Two mechanisms went quiet: the areaportal window's distance blend fell back to
the brush's renderamt — a solid `TOOLSBLACK` panel in every spawn window, which is B358's picture
again — and `UTIL_ComputeEntityFade` drew every entity at full alpha (B365).

**Why:** the owner, before it was measured — *"this is a stv demo, pvs should update based on the
camera, not a player themselves"*, *"that is basically guaranteed to be how valve does it"*. It is.
`CurrentViewOrigin()` is the render view's origin whatever drives it; the engine has no
first-person branch. A demo viewer is a spectator, so gating anything on "is there a player whose
eyes these are" is a question the engine never asks.

**How to apply:** the view origin comes from the DEVICE that built the frustum — `Device3D.Eye`,
the same field the cull and the 2D sky read — not from a player, and not from a second reading of a
camera. When something measures a distance from the viewer and looks right in first person, check
it in free look before believing it. And note the test shape that missed this for a month: every
unit test set `ViewOrigin` by hand, so all of them tested the arithmetic below the defect and none
tested how a frame supplies it.

See [[one-camera-or-the-cull-lies]], [[output-level-assertion-or-it-is-not-done]] and
[[a-dropped-field-falls-to-a-computed-default]].
