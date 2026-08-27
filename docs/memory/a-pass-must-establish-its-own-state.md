---
name: a-pass-must-establish-its-own-state
description: DrawDecals left alpha blending on and the props pass inherited it, so every static prop blended against its envmap mask; the leak surfaced in the code that moved, not the code that leaked.
metadata:
  type: project
---

**A render pass that does not set the state it needs will one day inherit a wrong one, and the
symptom will appear in whatever moved — not in what leaked.** `DrawDecals` turned alpha blending on
and never turned it off; the next reset lived two passes later inside `DrawTranslucent`. Under the
old order — world, props, decals — nothing ran in that gap. Then `e7b95cf` moved static props to
draw after the overlays, correctly, matching `CBaseWorldView::DrawExecute`. From that commit every
static prop in every map was alpha-blended, for two days.

**It hid because of WHICH alpha it blended against.** In a TF2 model material the base texture's
alpha is usually an envmap mask (`$basealphaenvmapmask`), not opacity. Shiny things mask low, dull
things mask high — so pipes became glass tubes, a dome became a soap bubble, a sign showed the wall
through it, and a silo's collar vanished, while every crate and wall looked perfect. Four unrelated
art faults, one line of state. Brushwork was untouched because it draws *before* the decals, which
made it look like a model-pipeline bug: four hypotheses were tested and cleared there — DXT upload,
alpha-test classification, VTX strip winding, back-face culling — before anyone looked at the pass
order.

**What found it was drawing one view two ways.** In the category view the collar was present and
orange; in the textured view it was absent. The category path returns from the pixel shader before
the lighting, after the clip, with alpha forced to one — so the fragment demonstrably survived the
alpha test and reached the output merger, and only the blend was left. Two screenshots ended a
two-day hunt.

**No test could fail on it, and the reason was the condition rather than the assertion.** Every
render test here drew hand-built quads with an alpha of one, and blending against an alpha of one is
arithmetically identical to not blending. The fix was to source the fixture from the map — an opaque
material that still carries a low alpha — and to skip loudly when no such material exists.

**How to apply:** every pass sets the blend, depth and rasteriser state it requires on entry, and
never relies on a previous pass having restored anything. When reordering passes, the suspect is not
the code you moved. And when a surface is present in a diagnostic view and absent in the real one,
the fragment is reaching the output merger — look at blending before looking at geometry.

Related: [[build-time-shortcuts-assume-the-camera]], [[instrument-bugs-outnumber-decoder-bugs]],
[[output-level-assertion-or-it-is-not-done]], [[instrument-bugs-outnumber-decoder-bugs]],
[[logs-are-the-debugger]].
