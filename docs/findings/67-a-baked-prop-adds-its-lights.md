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
- the static term is the colour mesh as this pipeline already lit it (`FromVertexByte`, `min(1, 2c)`) — no
  `GammaToLinear`. That is B426's existing choice, kept so an unlit baked prop draws exactly as before; whether
  it is right is a separate question about every baked prop, not this one.

*Interpolated:* the dlight flag test (`0xe`) is taken from the model draw's `0x1801b7a10`, because the
enumerator calling `FUN_1801b5260` has no direct cross-reference to read.

Not ported: when cvar `DAT_18069c368` is nonzero a second combined state (the instance's `+0x88..+0xcc` added to
the per-draw state, lights unioned to four, `0x1800f2120..0x1800f2232`) is built for a record at
`param_1 + 0x18`; its consumer is the second `FUN_1800f0e10` call, not the prop's own lighting.
