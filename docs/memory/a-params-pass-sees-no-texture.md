---
name: a-params-pass-sees-no-texture
description: A shader's InitParams runs before any texture loads, on every load and every switch reload, so an IsTexture() test there is always false — VertexLitGeneric's WantsSkinShader in InitParams routes only $basemapalphaphongmask 1 to the skin params pass (B720).
metadata:
  type: project
---

**`SHADER_INIT_PARAMS` sees every texture var as a string.** TF2's `materialsystem.dll` parses the VMT into vars
(`FUN_180008d20`; a texture name becomes a string var) and calls the shader's `InitShaderParams` straight after
(`FUN_18004fd60`); `LoadTexture` only happens later, in `SHADER_INIT`. `IsTexture()` is just the var's type
(`imaterialvar.h:143-146`). A `mat_phong`/`mat_bumpmap` change reloads with the vars freed (`Uncache( false )`,
`FUN_18000a600`), so a reload is no different.

**So read any `IsTexture()` in an InitParams as false.** The case that mattered: `WantsSkinShader` in
`InitParamsVertexLitGeneric_DX9` (`:105`) never sees a warp or bump map, so nearly every `$phong` material runs the
GENERIC params pass (its envmap undefinitions included) and then `InitSkin_DX9` (because `$phong` survives); only
`$basemapalphaphongmask 1` takes `InitParamsSkin_DX9`. The same function at draw time sees loaded textures and answers
differently. Part 3 of B720 assumed the opposite for `mat_bumpmap 0` and was wrong.

**How to apply:** before modelling "which init ran", ask what the init could see when it ran — params pass: strings;
init: loads; draw: textures.
