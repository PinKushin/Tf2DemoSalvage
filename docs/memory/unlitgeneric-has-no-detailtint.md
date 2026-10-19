---
name: unlitgeneric-has-no-detailtint
description: "UnlitGeneric declares no $detailtint; m_nDetailTint stays -1 and the helper writes c10 = white, so a VGUI/unlit material's $detailtint does nothing. Its detail mode is the VMT's (no ssbump override)."
metadata:
  type: project
---

**A shader's helper applies a parameter only if the shader maps it into its vars.** `UnlitGeneric` shares
`vertexlitgeneric_dx9_helper.cpp` but its `SetupVars` never assigns `m_nDetailTint`. The vars constructor memsets
0xFF, so the value stays -1, and the helper takes `SetPixelShaderConstant4( 10, 1, 1, 1, 1 )` (:988-993).
`$detailtint` on an UnlitGeneric material is dead.

The same helper reads `$detailblendmode` as written (:422). Only LightmappedGeneric forces an ssbump detail to mode
10 or 11, so the world's `MapDetail.Mode` is not a VGUI material's mode.

**Why:** the task asked to port `$detailtint` for VGUI. Porting it would have been a divergence. The general
lesson: before porting a parameter, check the shader's `SetupVars` (unlitgeneric_dx9.cpp:72-89), not only the
helper. See [[a-shader-param-default-string-is-not-applied]] and [[material-variables-split-three-ways]].

**How to apply:** `VguiShading.Read` builds the VGUI `MapDetail` with a white tint and the VMT's mode. Finding 78,
commit `c59b9e60`.
