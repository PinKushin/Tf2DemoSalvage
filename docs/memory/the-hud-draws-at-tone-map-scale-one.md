---
name: the-hud-draws-at-tone-map-scale-one
description: "Under integer HDR, CViewRender::Render resets the tone-map scale to 1 after post-processing, before VGUI paints — so HUD model panels draw unexposed, not at the scene's auto-exposure."
metadata:
  type: project
---

**The HUD, and every 3D model panel in it, draws at tone-map scale 1 under integer HDR.**
`CViewRender::Render` calls `SetToneMappingScaleLinear(Vector(1,1,1))` under `HDR_TYPE_INTEGER` after
`DoEnginePostProcessing` and `PerformScreenSpaceEffects`, and before the 2D section with `VGui_Paint`
(viewrender.cpp:2211-2216). `CPotteryWheelPanel::Paint` and `CMDLPanel::OnPaint3D` set no scale of their own.

**Why it mattered:** the class portrait drew at the scene's auto-exposure scale, about 20 gamma levels darker than TF2
(101 against 125 at the helmet; (101/125)^2.2 ≈ 0.63). The light rig was identical, so comparing lights found nothing.

**How to apply:**
- Anything drawn after post-processing in `Device3D` (HUD quads, panel models, panel particles) gets
  `ToneMapScaleAfterPostProcessing`.
- A log that reports "the scale this frame was drawn at" must carry the scene's value from before the reset
  (`_drawnScale`, see [[instrument-bugs-outnumber-decoder-bugs]]).
- Finding 78, commit `797427be`.
