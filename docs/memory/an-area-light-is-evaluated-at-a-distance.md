---
name: an-area-light-is-evaluated-at-a-distance
description: "emit_surface is a runtime 1/d² light the engine ranks and folds; its all-zero attenuation terms mean nothing to it, and a static prop's cube holds none of them (B669)."
metadata:
  type: project
---

`LocalLights.IsLocal` refused `emit_surface` from a real measurement and a wrong reason: through vrad's all-zero rule, 108
lights with no attenuation drew mid at luminance 6.3, which was read as "never evaluated at a distance, already in the leaf
cube". The engine gives that kind its own `1 / max(1, d²)` and one cosine, and skips it only for a LEAF-lit caller when
`DWL_FLAGS_INAMBIENTCUBE` is set. 0 of f12's 108 carry the flag. Full account: finding 66, "An `emit_surface` light".

**How to apply:** a blown-out result from a light proves the evaluation was wrong, not that the light should not be there. Before
excluding a kind, read what the engine does with that kind, not what the compiler's comment implies. And a shape is evidence:
TF2 minus ours constant down a cylinder meant the cube, which is what pointed at the writer of the cube.
Related: [[a-phong-static-prop-is-not-vertex-lit]], [[a-gap-can-be-filed-backwards]].
