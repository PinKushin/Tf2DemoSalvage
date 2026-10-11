---
name: a-shader-param-default-string-is-not-applied
description: The third argument of SHADER_PARAM is a display string the material system never applies — an unset shader parameter is zero (or 1,1,1 for a COLOR) unless the shader's SHADER_INIT_PARAMS sets it. Check INIT_PARAMS, not the declaration.
metadata:
  type: feedback
---

**`SHADER_PARAM( NAME, TYPE, "default", "help" )` does not set a default.** The string is what
`GetParamDefault` hands a tool. What an unset parameter holds is decided by
`CShaderSystem::InitShaderParameters` in the closed `materialsystem.dll`, after the shader's own
`SHADER_INIT_PARAMS` has run:

| type | value when nobody set it |
|---|---|
| `BOOL`, `INTEGER` | 0 |
| `FLOAT` | 0 |
| `VEC2`, `VEC3`, `VEC4` | zeros |
| `COLOR` | 1, 1, 1 |
| `TEXTURE`, `ENVMAP`, `STRING` | left undefined |

plus `$color` = 1,1,1 and `$alpha` = 1 for every shader. *Read in disassembly*, TF2 x64
`materialsystem.dll`: the loop at `0x18004fe52`-`0x18004ff40`, jump table at `0x18004ff74`.

**Why it is written down:** B391 read `SHADER_PARAM( IGNOREVERTEXCOLORS, SHADER_PARAM_TYPE_BOOL, "1", … )`
as "defaults to one", so the `Sprite` shader's additive mode was believed to drop the vertex colour.
It keeps it. Every spotlight shaft drew at full texture strength — six times too bright, unfaded —
and it took a golden comparison to notice (B521).

**How to apply:**

- A default that matters is set in `SHADER_INIT_PARAMS` with `if ( !params[X]->IsDefined() )`. If the
  shader has no such line for the parameter, the default is the table above, whatever the declaration says.
- **The tell was in the same file**: `sprite_dx9.cpp` declares `HDRCOLORSCALE` `"1.0"` and still sets it
  to 1 by hand, and declares `NOSRGB` `"0"` and sets it to ONE. A declaration that applied would make
  the first redundant and the second a bug. When a shader hand-sets a default equal to its declared
  one, that is Valve telling you the declaration does nothing.
- Any existing reading of the form "defaults to N (`file.cpp:<SHADER_PARAM line>`)" is suspect; cite the
  `INIT_PARAMS` line or the zero rule instead. Related: [[a-default-is-not-a-constant]],
  [[a-valve-comment-is-not-its-code]].
