# 67 — A baked prop adds its lights; it never drops its colours

**A correction to B424.** A static prop with a colour mesh (vrad's `.vhv`, or the CPU-lit mesh of
[65](65-an-unbaked-static-prop-is-lit-once.md)) beside a flickering or switched lamp.

## The wrong turn

The model draw, `engine.dll` `0x1800f1bd0`, handles a static prop's colour mesh in two branches, split by
one virtual call on the hardware config: slot `+0x150`. B424 read the first branch it met:
`FUN_1801bb8a0` (any dlight bit set) or `FUN_1801bb830` (a listed light's style animates) clears the
colour-mesh flag and takes full lighting, `FUN_1801ba590(handle, …, 7)`. It was ported as-is, with a
test on `koth_dryfield` asserting that placements under the style-1 flicker lose their colours.

The ambiguity was noticed in B425 step 1, when the dlight bit had to be wired and it led into the same
branch. B429 had already counted the slot from `imaterialsystemhardwareconfig.h`:
`SupportsStaticPlusDynamicLighting`, **true on DX9**. So B424's port is the branch TF2 on DX9 hardware
never takes. *Read from disassembly; the slot name is arithmetic over the published header.*

## What DX9 does

With the slot true the colour mesh is **kept**, `param_8 + 0x40` is set to 1 (the studio render's
static-plus-dynamic flag), and the draw's lighting state is `FUN_1801ba590(handle, …, 6 + (+0x1a4 != 0))`:

| bit | `FUN_1801ba590` (`0x1801ba590`) |
|---|---|
| 1 (absent) | the state starts at ZERO — no leaf cube, no style-0 light; those are in the colours |
| 4 | `FUN_1801b5400`: every light of the handle's list (`+0x1b0`, style nonzero, in PVS, in reach — `FUN_1801b6bf0`) at its **current** style value; cached at `+0x88`, rebuilt when a listed style's stamp passes `+0xf8` |
| 2 | each dlight whose bit is in `+0x1a4`, masked by the live set `DAT_1806996c4`, cleared if stamp `+0x1a8` is stale; `FUN_1801bb940` converts it, `FUN_1801b6550` ranks it |

`FUN_1801b5400` and `FUN_1801b6550` both rank into the **local-light list**, at most
`min(r_maxlights-style cvar, hardware max)` (four here), and fold an evicted light into the **cube**
(`FUN_1801b5db0`). A dlight is ranked at 100000 when evicting, so it always takes a slot. The bit is set by
`FUN_1801b5260`, called through an enumerator vtable (`0x1803adb80`) for each prop a dlight touches, when
the prop is in the dlight's PVS (`+0x38` on the static-prop manager).

So styled lights count **whether they animate or not** — a switchable style at `'m'` lights the prop,
at `'a'` it does not. The "animated" test belonged to the non-DX9 branch only.

## How the shader combines them

`PixelShaderDoLightingLinear` (`src/materialsystem/stdshaders/common_vertexlitgeneric_dx9.h:259-319`):

```
linear  = GammaToLinear( staticLightingColor * cOverbright )   // cOverbright = 2.0, :31
linear += AmbientLight( worldNormal, cAmbientCube )
linear += Σ PixelShaderDoGeneralDiffuseLight( … )              // up to four
```

**Additive.** The colour mesh is the static term, the cube is zero unless a light was evicted into it,
and each local light adds its N·L term. *Read from published source.*

## What the port does

`LevelLighting.StaticPlusDynamicLights` returns the flags-6/7 lights; `EntityModelSet` keeps the colours
and draws with a zero cube (`LevelLighting.NoStaticState`) and those lights; the model shader, told by
`ambientCube[1].w`, adds the colour term to the cube and lamps instead of multiplying. With no light
reaching, nothing changes: the colours alone.

Three simplifications, each in the code:
- the evicted light is dropped rather than folded into the cube, as `ModelLightingAt` already does;
- one ranking over styled and dynamic lights, without the dlight's eviction precedence (differs only past four);
- ~~the static term is the colour mesh as this pipeline already lit it (`FromVertexByte`, `min(1, 2c)`) — no
  `GammaToLinear`.~~ Settled the same day, below.

## The static term, settled from source

Every DX9 route through the vertex-lit shader reads the colour mesh one way: `DoLighting` and `DoLightingUnrolled` in
the vertex shader (`common_vs_fxc.h:870-874`, `:902`) and `PixelShaderDoLightingLinear` in the pixel shader
(`common_vertexlitgeneric_dx9.h:272`) all add `GammaToLinear( staticLightingColor * cOverbright )`, `cOverbright` 2.0
(`common_vs_fxc.h:59`), `GammaToLinear` `pow( x, 2.2 )` (`common_fxc.h:189-192`). Only `_X360` differs (`col * col`). No
HDR/LDR branch touches it, so which combo TF2 picks does not matter. *Read from published source.*

The bytes are the exact inverse. vrad writes each `.vhv` colour through `ConvertRGBExp32ToRGBA8888` →
`ConvertLinearToRGBA8888` → `LinearToVertexLight` (`vradstaticprops.cpp:1583-1586`, `lightmap.cpp:3553-3599`); `g_bHDR`
only picks the name, `sp_hdr_%d` or `sp_%d` (`:1534-1540`). `LinearToVertexLight` is the engine's `lineartovertex`
table, `min( 1, pow( L, 1/2.2 ) · 0.5 )` (`color_conversion.cpp:248-255`), which the CPU bake of
[65](65-an-unbaked-static-prop-is-lit-once.md) uses too. So `pow( 2c, 2.2 )` returns `L` within a byte step.
*Read from published source; round trip checked by arithmetic in the tests.*

**The wrong turn.** The port read the byte as `min( 1, 2c )` — the gamma-space value used as light — from when the
whole pipeline worked in display space (B54). It then also multiplied it by the model's white lightmap texel,
`1 · OverbrightScale` = 2. So the drawn static term was `min( 2, 4c )` against Valve's `pow( 2c, 2.2 )`. They cross
only near byte 227; below that the port was brighter, and at the reference map's average byte, about 59, it was
0.93 against 0.18 — five times too bright in linear, about twice on screen. That agrees with the measurement that
first called props "too dark" (0.23 raw against lightmaps at 0.47 in display space): `0.47^2.2` is 0.19, the same
light as `pow( 0.46, 2.2 )` = 0.18. A baked prop now matches the wall beside it.

Fixed in `PropModels.FromVertexByte` (one place, both `.vhv` and CPU-lit bytes) and in the model shader, which takes
the colour stream as the whole static term when one is bound (`ambientCube[1].w`, now written with or without a cube).

*Interpolated:* the dlight flag test (`0xe`) is taken from the model draw's `0x1801b7a10`, because the
enumerator calling `FUN_1801b5260` has no direct cross-reference to read.

Not ported: when cvar `DAT_18069c368` is nonzero a second combined state (the instance's `+0x88..+0xcc` added to
the per-draw state, lights unioned to four, `0x1800f2120..0x1800f2232`) is built for a record at
`param_1 + 0x18`; its consumer is the second `FUN_1800f0e10` call, not the prop's own lighting.
