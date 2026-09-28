---
name: a-pass-must-establish-its-own-state
description: DrawDecals left alpha blending on and the props pass inherited it, so every static prop blended against its envmap mask; the leak surfaced in the code that moved, not the code that leaked.
metadata:
  type: project
---

**A render pass that doesn't set the state it needs will one day inherit a wrong one, and the
symptom appears in whatever moved — not in what leaked.** `DrawDecals` turned alpha blending on and
never off; the next reset lived two passes later in `DrawTranslucent`. Under world→props→decals
order nothing ran in the gap. `e7b95cf` moved static props after overlays (correctly, matching
`CBaseWorldView::DrawExecute`) — from that commit every static prop in every map was alpha-blended,
for two days.

**It hid because of WHICH alpha it blended against:** a TF2 model's base-texture alpha is usually an
envmap mask (`$basealphaenvmapmask`), not opacity — pipes became glass, a dome a soap bubble, a
silo's collar vanished, while every crate looked perfect. Brushwork (drawn before decals) was
untouched, making it look like a model-pipeline bug; four hypotheses were cleared there (DXT upload,
alpha-test classification, VTX winding, culling) before anyone checked pass order.

**Found by drawing one view two ways:** category view showed the collar present and orange; textured
view showed it absent — meaning the fragment survived the alpha test and only the blend was wrong.

**No test could fail on it**, because every render test drew quads at alpha=1, where blending is
arithmetically identical to not blending. Fix: source the fixture from the map (an opaque material
that still carries low alpha), and skip loudly when none exists.

**How to apply:** every pass sets its own blend/depth/raster state on entry, never relying on a prior
pass having restored anything. When reordering passes, the suspect is not the code you moved. A
surface present in a diagnostic view and absent in the real one means the fragment reached the output
merger — check blending before geometry.

Related: [[build-time-shortcuts-assume-the-camera]], [[instrument-bugs-outnumber-decoder-bugs]],
[[output-level-assertion-or-it-is-not-done]], [[logs-are-the-debugger]].
