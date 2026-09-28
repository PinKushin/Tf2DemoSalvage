---
name: a-pov-demo-follows-the-recorder-in-everything
description: "POV playback shows only what the recorder saw - his cameras, his observer modes, whom he spectated in-eye; the viewer never chooses"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 7256e9d8-efff-49d6-8602-f5a3d8ea7240
  modified: 2026-09-22T19:41:26.522Z
---

A POV demo never lets the viewer choose the camera — it shows the recorded view (`democmdinfo`) in
every observer mode. "Me" (hidden body, drawn viewmodel) is the recorder, or his `m_hObserverTarget`
while `OBS_MODE_IN_EYE`. Deathcam/freezecam/chase change what's drawn, never where the view is. D128,
D153, D188.

Owner, 2026-09-22: *"POV demos dont allow you the viewer to change the camera at all it only follows
whatever the player who recorded did."* A chase camera built behind a dead recorder, and a
first-person that stayed in his eyes while he spectated someone, were both divergences (B417).

**How to apply:** anything asking "whose view" on a POV demo goes through `SpectatorView.Followed` —
never build a camera from the recorder's entity directly. Related:
[[sourcetv-has-a-local-player]], [[check-at-the-owners-moment]].
