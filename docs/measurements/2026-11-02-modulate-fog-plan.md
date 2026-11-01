# Plan and measurement 2026-11-02: Modulate and DecalModulate take Valve's grey fog (B637)

**Status: RUN.** Engine read first (this file's first two sections), then the conformance tests, then the shader change,
then the real frame. One deviation from that order, stated: the shader API read (Ghidra) was blocked by another agent's
lease for about fifteen minutes, so the tests and a first shader version were written against the reading in the SDK
alone (a grey of 128/255 everywhere). The shader API read then changed two things (Modulate's grey takes the tone-map
scale; height fog's grey is linear), and the tests for those two were added after it. Their red-for-the-right-reason
evidence is the sabotage section, not a first red run.

## What the owner sees

Every scorch, bullet hole and blast mark on a map with fog, and every Modulate model or brush (`koth_nucleus`'s
`mp_energy`), fades toward "change nothing" with distance, as in TF2, instead of staying at full darkness however far
away it is. Close up and on maps without fog nothing changes. Under water (a refraction view's height fog) a decal
fades toward a dark grey, which is Valve's arithmetic too (below).

## Valve's path (read)

**The pixel shaders** (*published source*):
- `decalmodulate_ps2x.fxc:37` `CalcPixelFogFactor( PIXELFOGTYPE, g_FogParams, eye.xyz, worldPos.xyz, projPosZ )`
  (the float3 overload, `common_ps_fxc.h:241`, so range, radial and height all reach it; `PIXELFOGTYPE` 0..2), then
  `:42` `fogFactor = pow( saturate( fogFactor ), 0.4f )`, then `:44` `FinalOutput( result, fogFactor, PIXELFOGTYPE,
  TONEMAP_SCALE_NONE )`. The combo is `GetPixelFogCombo1( true )` (`DecalModulate_dx9.cpp:197`).
- `modulate_ps2x.fxc:36` the legacy z-only overload (`common_ps_fxc.h:269`, which turns radial into range), no exponent,
  `:37` `FinalOutput( …, TONEMAP_SCALE_NONE, … )`; `PIXELFOGTYPE` 0..1 from `GetPixelFogCombo()` (`modulate_dx9.cpp:255`).
- `FinalOutput` (`common_ps_fxc.h:345-373`): NONE leaves the colour (`:356-358`), then `BlendPixelFog` toward
  `g_LinearFogColor` (c29): range or radial by the factor SQUARED after a saturate (`:286-290`), height by the factor
  itself (`:295-298`).
- Vertex fog: `CalcFog` returns 1, no fog, for height fog on vs20 ("We do this work in the pixel shader in dx9",
  `common_vs_fxc.h:571-575`). Range and radial vertex fog are computed (`:563-570`) but are the fixed-function path; the
  world here already draws pixel fog only (B139), and this change keeps that choice for these two shaders.

**The shadow state** (*published source*): DecalModulate `DisableFogGammaCorrection( true ); //fog should stay exactly
middle grey` and `FogToGrey()` (`DecalModulate_dx9.cpp:79-80`), sRGB read and write off (`:74-75`). Modulate
`FogToGrey()` under `$mod2x`, `FogToOOOverbright()` without (`modulate_dx9.cpp:201-209`), no
`DisableFogGammaCorrection`. The enum (`ishadershadow.h:209-215`): DISABLED 0, OO_OVERBRIGHT 1, BLACK 2, GREY 3,
FOGCOLOR 4, WHITE 5.

**The shader API, `shaderapidx9.dll` x64** (*disassembly*, 2026-11-02, Ghidra; addresses only):
- `0x180014200` is the fog-mode apply (its `SetRenderState( 0x22 = D3DRS_FOGCOLOR, … )` and the switch identify it).
  Modes 1 and 3 set r = g = b = 0x80 with gamma correction on; 2 black and 5 white with it off; 4 the scene's colour.
  The fourth argument (disable gamma correction) clears it. It stores the flag "no gamma correction" at `+0x130c`, and
  the pixel fog colour as **r/255 raw, whatever the flag** (`+0x12fc..+0x1304`). The gamma correction and tone-map
  scale it computes go only into the fixed-function `D3DRS_FOGCOLOR`.
- `0x180025a50` uploads c29 (`0x1d`) from that raw colour (the same fields at `+0x1684..+0x168c` on the base it is
  called with):
  - **range fog** (`+0x3a88 == 1`, only when `r_pixelfog` and the hardware caps say so): raw, converted to linear
    (vtable `+0x758`) only if `+0x2040` is set (`0x180025c10`). `+0x2040` is written by `0x180019970`, which also
    swaps the texture on sampler 15, the `SRGBOutput` gamma table: it is the sRGB-write state. These two shaders write
    without sRGB, so **raw 128/255**.
  - **height fog** (`== 2`): converted to linear unconditionally, `pow( c, 2.2 )` (`0x180025b2c`, the exponent the
    double at `0x18007e0d8`, 2.2) or vtable `+0x758` when a hardware-config query (`+0x1f0`) returns 0.
  - then **times the tone-map scale** (`+0x3a8c`) when HDR type is 1 (integer) **and the no-gamma-correction flag is
    clear** (`0x180025bec`, `0x180025c8b`).

**So, per shader** (*arithmetic on the read*):

| shader | range/radial fog colour | height fog colour | lerp weight, range | lerp weight, height |
|---|---|---|---|---|
| DecalModulate | 128/255 | pow(128/255, 2.2) = 0.2195 | (f^0.4)^2 = f^0.8 | f^0.4 |
| Modulate `$mod2x` | 128/255 · scale | 0.2195 · scale | f² | f |
| Modulate | 128/255 · scale | 0.2195 · scale | f² | f |

`scale` is `LINEAR_LIGHT_SCALE` under integer HDR, 1 otherwise. The lerp is on the shader's output, which for these
two is the stored (gamma) value, and the blend then multiplies stored values (`dst · 2s` or `dst · s`, B641).
**Two consequences that read as wrong but are Valve's:** plain Modulate in full fog halves what is behind it (0.5 is
neutral only for a ×2 blend); and under height fog every one of them fades toward a DARK grey (0.2195 · 2 = 0.44 for
mod2x), since the height path linearises the grey and these shaders never re-encode it.

**Unread, flagged:** which way the `+0x1f0` query goes on a TF2 machine (`pow 2.2` against vtable `+0x758`; our water
fog colour already takes `pow 2.2`, `WorldRenderer.Water.cs` `Linear`, so this follows it). *Interpolated.*

## Ours, before (read)

- `WorldRenderer.FogModeOf` gave every modulating material mode 2, none (`WorldRenderer.cs` at `56da2627`, ~6619).
- `PixelFog` skipped mode ≥ 1.5 (~902); the height-fog branch in the main pixel shader too (~2021).
- The B641 exit (~2044) decodes a modulating draw's value when no gamma twin is bound; the fog must precede it.
- `ModulateToneMapConformanceTests.FogModeAndToneMapType_AModulatingMaterial_IsFogModeTwoAndNoToneMap` was the tripwire.

## The change (minimal)

- `FogModeOf(surface, shader)`: DecalModulate mode 4, any other modulating material mode 3 (`WorldRenderer.cs:6662`,
  constants `:6645`, `:6648`), from the one caller that already holds the shader name (`:3912`).
- In the shader: `Fogs(mode)` admits 0, 1, 3 and 4; `FogTarget(mode, colour, grey)` (`:917`) returns the grey,
  multiplied by `debugModes2.y` (the linear light scale) for mode 3 only; `ModulateFogFactor` applies `pow( f, 0.4 )` for
  mode 4. `PixelFog` (`:931`) passes `StoredGrey` (128/255, `:906`); the height branch (`:2053`) passes `LinearGrey`
  (0.2195). The height branch's `* ToneMapScale(tintControl.w)` is unchanged, so a modulating draw there stays
  unscaled except for its grey.
- The single gamma exit (`:2078`) is untouched: both branches fog in gamma before it, so the no-twin fallback decodes a
  fogged value.
- Not changed: the alpha a modulating draw writes inside a refraction view (Valve's DecalModulate writes none,
  `DecalModulate_dx9.cpp:71`; ours writes `albedo.a` with its blend). Pre-existing, outside fog; reported.

## Tests (synthetic, `ModulateFogConformanceTests`, 17; tripwire replaced in `ModulateToneMapConformanceTests`)

An identity camera puts the quad at clip z 0.9: range fog over 0..0.9 makes max density the factor, and a water
surface at −0.9 with a range of 1.8 makes the height factor exactly 0.5. Expected values are Valve's arithmetic above,
±2 levels. Cases: the unfogged control per material (47, 47, 116); half range fog (decal 128, Modulate 59, mod2x 145);
half height fog (decal 74, Modulate-128 68, mod2x 109); full range fog (decal ≈ 189, Modulate ≈ 94); Modulate's scaled
grey at scale 0.5 (≈ 47) beside DecalModulate's unscaled one (≈ 189); the height branch at scales 0.5 and 2 equal to 1
for DecalModulate; the no-twin fallback under full fog (128: fogged in gamma, then decoded); and the lit control under
full red fog (255, 0, 0), which proves the fog reaches the draw.

**First run, before any shader change: 10 red, 6 green**, every red one at its unfogged value (47, 47, 116, and the
fallback at 32), the controls green.

## Real frame

`ctf_crasher`, `crasher_scorch.dem` tick 2000, camera `-3116 -1344 300 89 0` (B639/B641's), Debug `--shot`, 984x551,
`+mat_force_tonemap_scale 1`. Scorch hidden for the control by a scratch build of `56da2627` whose DecalModulate alpha
cutoff is 2 under an env switch (`TF2VIEW_B637_HIDE`), so nothing modulating draws. `r_drawentities 0` was tried first
and does NOT hide the scorch brush (base and hidden came out identical): kept as an instrument note.
Instrument: `D:/measure-f12/decal-cmp/b637/scripts/ratios.py`, median per-pixel on/off of stored and linear values per
region (findings 18's regions). **Control:** on TF2's own pairs it gives ring 0.727, centre 0.580 stored with fog off,
against B641's recorded 0.725 and 0.591.

| region (stored on/off) | TF2 fog off | TF2 fog on | ours before | ours after |
|---|---|---|---|---|
| alpha-0 surround (both) | 1.000 | 1.000 | 1.000 | 1.000 |
| ring | 0.727 | 0.774 | 0.705 | 0.751 |
| centre | 0.580 | 0.647 | 0.563 | 0.632 |

**The fog's own share** (on over off): TF2 ring ×1.065, centre ×1.116; ours (after over before) ×1.065 and ×1.123. What
is left is the 2-3% interior residue B641 already carries, unchanged by this. Repeat control (the before build shot
twice): 0 change in every region. Hidden frame against B641's: 1.000 everywhere. The "after" was shot on the first
shader version and again on the final one (`after/base2-t2000.png`): identical ratios, as expected, since the shader
API changes touch only Modulate's grey and height fog, neither in this frame.

Pixel diff before against after (`pair-diff`): 195,596 px changed, max 21 levels, box (43,0)-(932,424), the scorch
and nothing else. Images: `D:/measure-f12/decal-cmp/b637/` (`before/`, `after/`, `sbs-before-after.png`).

## What is unsettled

- The `+0x1f0` branch of the height-fog linearisation (pow 2.2 taken, as the water fog colour already is).
- Height fog on a real frame: not measured (no refraction view with a decal in a captured frame).
- Modulate's tone-map-scaled grey on a real frame: not measured; held by the synthetic test only.
- A modulating draw's alpha write in a refraction view (above).
