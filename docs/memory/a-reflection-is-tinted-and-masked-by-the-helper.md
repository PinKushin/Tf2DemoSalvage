---
name: a-reflection-is-tinted-and-masked-by-the-helper
description: "$envmaptint is gamma on VertexLitGeneric/UnlitGeneric and raw on skin/LightmappedGeneric; a $phong reflection is always masked; a glow's occlusion is a GPU fraction (B521, B378)."
metadata:
  type: project
---

**A model's reflection is decided by the shader HELPER, not by the keys the VMT names.**

- `$envmaptint`: `VertexLitGeneric` without `$phong` and `UnlitGeneric` send it through `GetLinearVecValue`
  (`vertexlitgeneric_dx9_helper.cpp:1094-1101`, `commandbuilder.h:279-287`) — `[.2 .2 .2]` is 0.029. The skin path
  (`$phong`) and `LightmappedGeneric` send the numbers as written. Code: `ModelMaterialShading.EnvMapTint`.
- A `$phong` material's reflection is ALWAYS masked (`skin_ps20b.fxc:228-242`): the base map's alpha as it is, or the
  bump alpha with `$normalmapalphaenvmapmask`, inverted by `$invertphongmask`. `$basealphaenvmapmask` is not read
  there. Code: `ModelMaterialShading.SkinEnvMapMask`. Not modeled: `$envmapfresnel`.
- A glow or beam halo is dimmed by `PixelVisibility_FractionVisible`, a GPU occlusion count over a proxy quad
  (`c_pixel_visibility.cpp`); a lamp recessed in its fixture reads 0.7-0.8 at a slant. Code: `PixelVisibility`
  (Scene), `OcclusionQueries` (Render). The fade uses the frame's REAL time, not the demo's, and a `--shot` waits
  for `Settled`.

**How to apply:** when a reflective model reads too bright against TF2, switch the term off in the shader and
re-shoot before reading anything — and do it by hand: `+mat_specular 0` / `+mat_fullbright 1` on a `--shot` command
line log as set and change nothing (open, RISKS B521). A byte-identical "after" is the control telling you the
switch did not reach the draw.
