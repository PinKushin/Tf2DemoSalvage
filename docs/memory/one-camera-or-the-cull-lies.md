---
name: one-camera-or-the-cull-lies
description: "Anything derived from a camera — projection, frustum, LOD, sort — must come from one camera object passed once, never rebuilt from the same inputs."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-28T03:57:07.345Z
---

When a second thing starts being derived from the camera, pass **the camera**, not the thing you
already derived from it. `Device3D.SetCamera` took a `float[]` matrix; adding frustum culling meant
it needed six planes as well, and the tempting shapes — a second `SetFrustum` call, or inverting the
matrix back into planes — are both a **second derivation of the camera**.

Take the camera object and produce both from it in one place. Upstream, make "which camera is this
frame seen through" a single function (`ViewCamera.Active`) that the matrix path also goes through,
so the two cannot answer differently.

**Why:** the failure is invisible until it is dramatic. A frustum built from the free camera while
the picture is drawn through a player's eyes culls exactly the geometry the viewer is looking at —
in first person only, and only once the two cameras diverge. This project has already shipped the
neighbouring version: a build-time top-down culling shortcut that broke the moment the free camera
moved. See [[build-time-shortcuts-assume-the-camera]] and [[one-place-or-it-drifts]].

**How to apply:** the test is not "are these values equal now" but "can they ever disagree". If two
call sites each compute a camera-derived value from the same raw inputs, they can. Keep the raw
inputs behind one accessor and derive everything past it. Where an old overload must stay — here the
viewmodel pass, which has a camera of its own — have it leave the derived state ALONE rather than
clearing it or reconstructing it, and say in the doc comment which callers rely on that.
