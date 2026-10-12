---
name: a-seek-rebuilds-what-playing-accumulated
description: "A catch-up after a seek must step each past tick with the inputs of THAT tick; a stopped effect still follows its points; a playing TF2 frame is matched at the HUD's tick minus the interpolation delay (B527)."
metadata:
  type: project
---

**Anything the engine builds up frame by frame, a seek has to rebuild from what each frame had — not from today's.**

- **The medigun beam (B527).** `ParticleEffects.Bursts` caught a beam up over 132 ticks with both control points
  where they are now, and `Fade` (a stopped effect) never set control point 0 at all. Both read as a particle-operator
  fault — "thin, collinear, in the wrong place" — and the operators were right. The engine reads a followed point
  every frame (`UpdateControlPoint`, particle_property.cpp:527) and `StopEmission` only stops emitters
  (particle_property.cpp:251): the dying second of a beam keeps bending after the medic.
- **`ParticleBurst.History`** is the seam: asked only for ticks before the one shown, so a playing frame never pays.
  `FollowedPoint.Then` carries an attachment back rigidly with its player's recorded origin and yaw; the skeleton's
  own swing is not rebuilt, and the doc says so.
- **A paused `--shot` cannot show anything that only steps while playing** — the viewmodel bob (B525) is built and
  unseen for this reason. Say "unseen", do not shoot it paused and call it checked.

**Matching a PLAYING TF2 reference:** its HUD health is the newest tick, its drawn players are an interpolation delay
older (ten ticks on f12). The paused shot that matches is the HUD's tick MINUS the delay. Matched on health alone, the
medic had already jumped in ours and the beam pointed at the sky; an hour went to the particle definition before the
medic's Z was printed.

**Before reading an effect's definition for a fault, print where its two ends are** in both pictures. Direction
wrong = the inputs; shape wrong with the ends right = the operators.

**`MovementLock` reads a control point at the world origin as "none seen yet"** (the engine's own test). A synthetic
test that parks a point at (0,0,0) sees no first delta — start it anywhere else.
