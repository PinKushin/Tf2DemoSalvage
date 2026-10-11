---
name: a-model-shaders-color-is-gamma
description: "$color/$color2 on VertexLitGeneric, UnlitGeneric, skin and UnlitTwoTexture are gamma colours converted to linear at bind; UnlitGeneric reads no light at all (B514)."
metadata:
  type: project
---

**A model shader's modulation is gamma, and an unlit shader is unlit.**

- `SetModulationPixelShaderDynamicState_LinearColorSpace` (`BaseVSShader.cpp:652-661`): `c > 1 ? c : GammaToLinear( c )`.
  Used by `vertexlitgeneric_dx9_helper.cpp:1456`, `skin_dx9_helper.cpp:731`, `unlittwotexture_dx9.cpp:218`.
  `LightmappedGeneric` sends the raw numbers.
- `GammaToLinear` (`mathlib/color_conversion.cpp:273-291`): 0 below 0, 1 from 0.95, else `pow( round( c*255 )/255, 2.2 )`.
- `UnlitGeneric`: `hasDiffuseLighting = bVertexLitGeneric` (`vertexlitgeneric_dx9_helper.cpp:394`); diffuse is 1
  unless `$vertexcolor`. No baked colours, cube, sun or lamp.
- Code: `ModelMaterialShading`; applied in `WorldRenderer.SetMaterial` AFTER proxies (they write gamma too), and
  `combine.w` in the shader.

**How to apply:** a quoted engine line is not a ported one — findings 26 quoted the conversion for another point and
it was never applied. When a doc cites a line, check the code does what the line says.
`mat_drawflat` whitens AFTER the modulation, so it cannot be the albedo in a test about `$color`.
Probe: `prop-shade <map> <model substring>`.
