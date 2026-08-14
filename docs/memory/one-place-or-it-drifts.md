---
name: one-place-or-it-drifts
description: A fix belongs in exactly one place; anything copied or kept in step between files goes out of sync.
metadata:
  type: feedback
---

Every fix should land in a **single place**. If a change needs the same information copied into two
files, or two sites kept in step by hand, they will go out of sync.

**Why:** stated by the owner directly — "pretty much every fix should be in a single place, if we
run into a place we are having to copy or synchronize the information between files, they are going
to get out of sync." Said after watching exactly that failure: players all faced north because
`m_angRotation` was being read for them in `RecordProp`, while the comment naming
`m_angEyeAngles` as TF2's real facing property sat in a different method of the same file. Two
places, one of them right, and the wrong one was the one that ran.

**How to apply:** put the fix at the point the data is produced, not at each point it is consumed.
The eye-angle fix is one line in `RecordProp` because the pose it writes already feeds the
interpolator, `ScenePlayer` and the renderer — so position and angle cannot drift apart. Setting a
`Yaw` field on `ScenePlayer` instead would have been the same behaviour with two sources of truth,
and would have needed a second edit every time the angle logic changed.

The corollary for design: when a feature will be reached from two paths (a POV camera and a free
camera, say), build one thing that both call with a flag, not two implementations that agree today.

Related: [[logs-are-the-debugger]] is how the drift gets *found*, and
[[differential-beats-fixtures]] is why a second implementation cannot check the first.
