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
  world here already draws pixel fog only (B139), and this change keeps that choice for these two shaders. *Measured,
  not read:* on `ctf_crasher` TF2's fog changes the DecalModulate scorch by the same factor ours does with one pixel fog
  (below), so no second fixed-function fog stacks on it there. That covers DecalModulate under range fog only.
- **Radial fog: DecalModulate takes it, Modulate does not.** The legacy overload Modulate calls turns
  `PIXEL_FOG_TYPE_RANGE_RADIAL` into range (`common_ps_fxc.h:272-273`), so on an official map (radial forced, findings
  69) a Modulate surface fogs by projected depth while a DecalModulate one fogs by distance.

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
    double at `0x18007e0d8`, 2.2) or vtable `+0x758` when a hardware-config query (`+0x1f0`) returns 0. **Settled in
    review (below): the default TF2 machine takes `+0x758`, the sRGB curve.**
  - then **times the tone-map scale** (`+0x3a8c`) when HDR type is 1 (integer) **and the no-gamma-correction flag is
    clear** (`0x180025bec`, `0x180025c8b`).

**So, per shader** (*arithmetic on the read*):

| shader | range/radial fog colour | height fog colour | lerp weight, range | lerp weight, height |
|---|---|---|---|---|
| DecalModulate | 128/255 | sRGB⁻¹(128/255) = 0.2159 (was 0.2195 by pow 2.2; review) | (f^0.4)^2 = f^0.8 | f^0.4 |
| Modulate `$mod2x` | 128/255 · scale | 0.2159 · scale | f² | f |
| Modulate | 128/255 · scale | 0.2159 · scale | f² | f |

`scale` is `LINEAR_LIGHT_SCALE` under integer HDR, 1 otherwise. The lerp is on the shader's output, which for these
two is the stored (gamma) value, and the blend then multiplies stored values (`dst · 2s` or `dst · s`, B641).
**Two consequences that read as wrong but are Valve's:** plain Modulate in full fog halves what is behind it (0.5 is
neutral only for a ×2 blend); and under height fog every one of them fades toward a DARK grey (0.2159 · 2 = 0.43 for
mod2x), since the height path linearises the grey and these shaders never re-encode it.

**First flagged as unread, then read in review (below):** which way the `+0x1f0` query goes on a TF2 machine. The first
version took `pow 2.2`, following our water fog colour (`WorldRenderer.Water.cs` `Linear`). That was wrong for the
default configuration.

## Ours, before (read)

- `WorldRenderer.FogModeOf` gave every modulating material mode 2, none (`WorldRenderer.cs` at `56da2627`, ~6619).
- `PixelFog` skipped mode ≥ 1.5 (~902); the height-fog branch in the main pixel shader too (~2021).
- The B641 exit (~2044) decodes a modulating draw's value when no gamma twin is bound; the fog must precede it.
- `ModulateToneMapConformanceTests.FogModeAndToneMapType_AModulatingMaterial_IsFogModeTwoAndNoToneMap` was the tripwire.

## The change (minimal)

- `FogModeOf(surface, shader)`: DecalModulate mode 4, any other modulating material mode 3 (`WorldRenderer.cs:6665`,
  constants `:6648`, `:6651`), from the one caller that already holds the shader name (`:3915`).
- In the shader: `Fogs(mode)` admits 0, 1, 3 and 4; `FogTarget(mode, colour, grey)` (`:917`) returns the grey,
  multiplied by `debugModes2.y` (the linear light scale) for mode 3 only; `ModulateFogFactor` applies `pow( f, 0.4 )` for
  mode 4. `PixelFog` (`:931`) fogs mode 3 by projected depth even under radial fog, and passes `StoredGrey` (128/255,
  `:906`); the height branch (`:2056`) passes `LinearGrey` (0.2195). The height branch's `* ToneMapScale(tintControl.w)`
  is unchanged, so a modulating draw there stays unscaled except for its grey.
- The single gamma exit (`:2081`) is untouched: both branches fog in gamma before it, so the no-twin fallback decodes a
  fogged value.
- Not changed at first: the alpha a modulating draw writes inside a refraction view (Valve's DecalModulate writes none,
  `DecalModulate_dx9.cpp:71`). Fixed in the review batch (below).

## Tests (synthetic, `ModulateFogConformanceTests`, 19; tripwire replaced in `ModulateToneMapConformanceTests`)

An identity camera puts the quad at clip z 0.9: range fog over 0..0.9 makes max density the factor, and a water
surface at −0.9 with a range of 1.8 makes the height factor exactly 0.5. Expected values are Valve's arithmetic above,
±2 levels. Cases: the unfogged control per material (47, 47, 116); half range fog (decal 128, Modulate 59, mod2x 145);
half height fog (decal 74, Modulate-128 68, mod2x 109); full range fog (decal ≈ 189, Modulate ≈ 94); Modulate's scaled
grey at scale 0.5 (≈ 47) beside DecalModulate's unscaled one (≈ 189); the height branch at scales 0.5 and 2 equal to 1
for DecalModulate; radial fog at the view's left edge (Modulate by depth ≈ 85, DecalModulate by distance ≈ 189); the
no-twin fallback under full fog (128: fogged in gamma, then decoded); and the lit control under
full red fog (255, 0, 0), which proves the fog reaches the draw.

**First run, before any shader change: 10 red, 6 green**, every red one at its unfogged value (47, 47, 116, and the
fallback at 32), the controls green.

**Sabotage, one change at a time, each restored by its inverse edit** (fog class, 17 then 19; plus the tone-map class
for the fourth):

| broken | reddened |
|---|---|
| Modulate's grey not tone-map scaled | the Modulate case of `…AtHalfToneMapScale…` (1) |
| height fog toward 128/255 instead of the linear grey | all three `…HalfHeightFog…` (3) |
| DecalModulate's `pow( f, 0.4 )` dropped | the decal cases of half range and half height (2) |
| DecalModulate classified as Modulate (mode 3) | the decal cases of half range, half height, scale 0.5, both height-scale cases, and the mode test (6 of 42) |
| the height branch scaled by the linear light scale for every material | both `…HeightFogAtAnyToneMapScale_IsUnscaled` (2) |
| Modulate fogged radially (the shader before the radial fix; first run of the new test) | the Modulate edge case: drew 94, the radial value, against range's 85 (1) |
| DecalModulate denied radial fog | the DecalModulate edge case: drew 177, the range value, against radial's 189 (1) |

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

**The ratio record** (review): every on/off number above comes from this one script, run as

```
python -I D:/measure-f12/decal-cmp/b637/scripts/ratios.py <on.png> <off.png>
```

| pair (on, off) | ring stored | centre stored | surround L/R |
|---|---|---|---|
| `b639/tf2-live-z300-nofog-on.png`, `…-nofog-off.png` (TF2 fog off) | 0.727 | 0.580 | 1.000 / 1.000 |
| `b639/tf2-live-z300-on.png`, `…-off.png` (TF2 fog on) | 0.774 | 0.647 | 1.000 / 1.000 |
| `b637/before/base-t2000.png`, `b637/before/hidden-t2000.png` | 0.705 | 0.563 | 1.000 / 1.000 |
| `b637/after/base-t2000.png`, `b637/before/hidden-t2000.png` | 0.751 | 0.632 | 1.000 / 1.000 |
| `b637/review/base-t2000.png`, `b637/before/hidden-t2000.png` (review build) | 0.751 | 0.632 | 1.000 / 1.000 |
| `b637/before/base2-t2000.png`, `b637/before/base-t2000.png` (repeat control) | 1.000 | 1.000 | 1.000 / 1.000 |

The fog shares are the quotients: TF2 0.774/0.727 = 1.065 and 0.647/0.580 = 1.116; ours 0.751/0.705 = 1.065 and
0.632/0.563 = 1.123 (*arithmetic on the table*). Paths are under `D:/measure-f12/decal-cmp/`.

## Review batch (2026-11-02, branch `fix/b637-review-batch`)

**The height-fog grey's curve, read.** The question was whether DecalModulate's disabled gamma correction also skips
the height path's linearisation. It does not. In `0x180025a50` the height branch runs from `0x180025b08` to the
conversion at `0x180025b2c`/`0x180025b9a`, and the no-gamma-correction byte (`+0x1694`) is first read at `0x180025bec`,
after it, where it only skips the tone-map multiply. So the scorch's height-fog grey is linear for every shader, and
the reviewer's ~154 does not apply. **Which conversion:** the hardware-config slot `+0x1f0` (vtable `0x18007a198`, entry
`0x180004bc0`) reads the convar `r_shader_srgb` ("-1 = use hardware caps. 0 = use hardware srgb. 1 = use shader
srgb(software lookup)", registered at `0x180001120`, default "0"): 0 returns 0, 1 returns 1, -1 a caps bit. At the
default the code takes vtable `+0x758` (`0x18001aed0`, a thunk to `0x18003ddf0`), the sRGB decode (`x ≤ 0.04045 ? x ·
0.07739938 : pow( ( x + 0.055 ) / 1.055, 2.4 )`). `pow 2.2` is the `r_shader_srgb 1` path. **So the grey is 0.2159, not
0.2195** (*disassembly*; that TF2 users run `r_shader_srgb 0` is the shipped default, *read*, not measured on a client).
Changed in the shader and the tests. The test window cannot tell the two curves apart: they differ by 1.7%, under two
8-bit levels in every case here, so this choice is held by the reading, not by a test (effect below resolution).
**Wider, not fixed here:** the same height path converts the WATER's own fog colour, which ours converts by `pow 2.2`
(`WorldRenderer.Water.cs` `Linear`); and range fog's scene colour under sRGB write goes through the same `+0x758`,
where `FogConstants` uses `pow 2.2`. Both are the lit materials' fog, outside B637; reported to the lead.

**Alpha writes.** DecalModulate `EnableAlphaWrites( false )` (`DecalModulate_dx9.cpp:70-71`), Modulate
`EnableAlphaWrites( bWriteZ && bFullyOpaque )` (`modulate_dx9.cpp:211`), so neither writes destination alpha (Modulate
only with `$writez` on a fully opaque material, not modelled). Both modulate blend states now write colour only
(`BlendStates.Modulate`, `BlendStates.ModulateTwice`, the latter moved out of `WorldRenderer`), so a scorch in a refraction
view no longer overwrites the height-fog factor the water reads back. Held by
`BlendStateConformanceTests.BlendState_ModulateAndModulateTwice_WriteColourButNoAlpha`, red first.

**Tests (review):** the scale case now uses Modulate 128 (texel 64 made the right answer equal the unfogged control);
Modulate under height fog at scale 0.5, from the shader's formula `lerp( 128/255, 0.2159 · 0.5, 0.5 ) · 1` = 0.3050,
188 · 0.3050 ≈ 57; the no-twin fallback on the height path (≈ 49); and the translucent world after a model at another
scale under range and height fog, equal to the one-go draw. The harness's reapplied camera now restores the water view.
`ModulateFogConformanceTests`: 23.

| sabotage (review) | reddened |
|---|---|
| Modulate skips range fog when the scale is not 1 | the Modulate-128 scale case, and the translucent-after-model range case (2) |
| Modulate's scale moved from the grey to the height branch's outer multiply | `…ScalesOnlyTheGrey`, drew 34 as predicted (1) |
| height branch skips modulating materials | the three height cases, the height fallback, `…ScalesOnlyTheGrey`, the height translucent case (6) |
| harness drops the water view on reapply | the height translucent case: 94 against 58 (1) |
| harness drops the tone-map scale on reapply | both translucent cases and the two older `…AfterAModelAtAnotherScale…` (4) |

**Real frame on the review build** (`review/base-t2000.png`): ratios identical to the merged build (table above);
`pair-diff` against `after/base-t2000.png` changes 107 px by at most 2 levels, all in box (425,0)-(575,85), the HUD at
the top, nothing on the scorch. That is expected: the frame has range fog only and a DecalModulate scorch, whose grey
the review did not change.

## What is unsettled

- Which `r_shader_srgb` a given user runs (the default 0 is taken).
- The water fog colour and the scene fog colour under sRGB write take `pow 2.2` in ours, the sRGB curve in shaderapi
  (lit materials, outside B637).
- Height fog on a real frame: not measured (no refraction view with a decal in a captured frame).
- Modulate's tone-map-scaled grey on a real frame: not measured; held by the synthetic test only.
- Modulate's grey takes `LINEAR_LIGHT_SCALE`, so it inherits B636's stale exposure on frames without a 3D-sky or water
  view.
- `r_drawentities 0` leaving the scorch `func_brush` drawn: TF2's toggle hides brush entities. Not looked into here.
