---
name: a-pose-time-gate-misses-what-the-step-adds-after
description: "A per-frame gate read during the pose (\"only props holding a decal\") misses decals the effects step adds after the pose; a paused frame poses once per build, so the miss is permanent (B642 review batch 2)."
metadata:
  type: feedback
---

**Before gating pose-time work on state another presenter writes, check the frame order.**
- `ViewerPresenter.Device`: `PoseOver` (pose) → `_effects.DrawOver` (decals, particles, lights) → `DrawScene`.
- `MomentPresenter.PoseNow` poses once per build (`_posed`), so a paused viewer never poses again.

B642's first review made the static-prop decal light lazy: computed only if the pool held a decal for the prop at pose
time. The decal replay places static-prop decals in `DecalEffects.StepOver`, after the pose. So on a map's first frame,
after a seek, and on each new hit, the decal drew under the wrong light. Paused, it stayed wrong. The filed claim "lit
from the next frame, and a `--shot` showed no lag" was believed on one shot.

**Fix shape:** keep the gate, and add a pass after the step that fills what the pose missed (`LightDecalHolders` into
the pool's late lights). Write it after the device's wait (`FinishDrawBeforeEffects`), where the step already writes,
so no in-flight draw reads it.

**Test shape:** seed the state after the first pose, the step's real timing. Assert the light at the first draw, and
cover a paused seek: clear, then re-seed, with no pose between.

Related: [[one-demo-player-serves-camera-and-recorder]], [[a-paused-frame-is-two-moments]].
