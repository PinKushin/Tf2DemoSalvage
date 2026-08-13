# What parity with Source's renderer actually requires

The goal is not "an overhead map that reads well" but a correct Source renderer — because the
first-person view needs one, and because once at parity the extra headroom of DX11 over DX9 can go
somewhere useful.

Parity is a finite list, and the map itself states it. Measured on `cp_process_f12` by reading every
material's VMT and weighting by the world area its surfaces cover — the same method that found
`tools/toolsblack` covering 4.8 million units while a brightness ranking had put a 4,096-pixel tool
texture at the top.

## Shaders the map uses

| area | materials | shader | state |
|---|---|---|---|
| 122,411,259 | 102 | `LightmappedGeneric` | drawn |
| 18,340,886 | 3 | `WorldVertexTransition` | drawn, two textures mixed by vertex alpha |
| 11,363,671 | 30 | `LightMappedGeneric` | same, and note the capitalisation differs |
| 9,022,902 | 46 | `Patch` / `patch` | resolved through to the included material |
| 1,538,304 | 1 | `UnlitTwoTexture` | **not implemented** |
| 154,588 | 5 | `UnlitGeneric` | drawn as ordinary albedo |

**Shader names vary in case across the same map** — `LightmappedGeneric`, `LightMappedGeneric`,
`Patch`, `patch`. Anything matching them must do so case-insensitively; this project already does,
and it is the kind of thing that silently drops thirty materials.

## Features the map asks for

| area | materials | key | state |
|---|---|---|---|
| 117,465,699 | 41 | `$surfaceprop` | not rendering — physics and footstep sound |
| **54,857,158** | **21** | **`$bumpmap`** | **not implemented** |
| **36,327,110** | **36** | **`$detail`** | **not implemented** |
| 18,340,886 | 3 | `$basetexture2` | done |
| 1,538,304 | 1 | `$additive` | done, second pass, SRC_ONE DEST_ONE |
| 1,451,017 | 63 | `$translucent` | approximated by the alpha-test clip; real blending needs sorting |
| 591,872 | 2 | `$alphatest` | done |
| 123,932 | 4 | `$selfillum` | not implemented, and small |

## The order to do them in, and why

1. **`$detail`** — 36 million units, and the cheapest of the two big ones: a second texture multiplied
   in at a scale the material states, which is most of what a surface looks like from close range and
   nothing at all from above. Needed the moment there is a first-person camera.
2. **`$bumpmap`** — 55 million units, and the expensive one, because TF2's lightmaps for a bumped
   material store **four sets of luxels per sample**: one flat and three in the bump basis. Reading
   only the first is what this project does now, which is right for unbumped faces and throws away
   three quarters of the data for the rest. Verify against the lump arithmetic before writing any of
   it — the existing check that samples tile the lighting lump to exactly 100.0% is the instrument
   that will say whether bumped faces are already being walked correctly.
3. **`$translucent` sorting** — 63 materials but small area. The alpha-test clip standing in for it is
   wrong in a way that matters more in first person than from above.
4. **`UnlitTwoTexture` and `$selfillum`** — one material each in practice.

## What this does not cover

HDR and tone mapping, water, the 3D skybox drawn as sky rather than culled, `$phong` and rim lighting
on models, and decals or overlays. Each is real; none is on this map's critical path by area, and the
list above is the part that can be checked off against a number.
