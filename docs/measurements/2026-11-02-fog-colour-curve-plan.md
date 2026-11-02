# B653 — the fog colour's gamma-to-linear curve (plan, 2026-11-02)

Branch `fix/fog-colour-curve`, from `804f49dd` (the B637 review merge). RISKS B653. Found in B637's review batch
(`docs/measurements/2026-11-02-modulate-fog-plan.md`, "Review batch").

## Valve's path (*disassembly*, `shaderapidx9.dll`, read-only in `D:\ghidra-proj\tf2shaderapi`)

The pixel-shader fog colour, c29 (`g_LinearFogColor`, `common_ps_fxc.h:45`), is converted on the CPU at upload, in
`FUN_180025a50`, and never in the shader. The raw colour is the material system's gamma 0..1 colour at `this+0x1684`,
`+0x1688`, `+0x168c`. The fog mode is at `+0x3a88`.

| step | address | what it does |
|---|---|---|
| mode switch | `0x180025a62`-`0x180025a87` | 0 none: c29.rgb = 0. 1 range: go to `0x180025ae7`. 2 height (water): go to `0x180025b08` |
| range gate | `0x180025ae7`-`0x180025aff` | hardware-config `+0x1c0` and a ConVar at `0x1800a2378` (`+0x58`) must both be true, else c29.rgb = 0. Not identified; TF2 draws coloured fog, so on a live client both hold (*interpolated*) |
| range colour | `0x180025c10`-`0x180025c86` | raw colour; **if the sRGB-write byte `+0x2040` is set**, each channel goes through vtable `+0x758` |
| height colour | `0x180025b1f`-`0x180025be7` | hardware-config `+0x1f0` (the `r_shader_srgb` query, `0x180004bc0`): nonzero, `pow( x, 2.2 )` (`0x18005aa10`, exponent at `0x18007e0d8`, 2.2 as a double from a float); **zero, the default, vtable `+0x758`** |
| tone-map scale | `0x180025bec`/`0x180025c8b`, `0x180025c98`-`0x180025cd7` | unless `DisableFogGammaCorrection` (`+0x1694`), and only under `HDR_TYPE_INTEGER` (`+0x1b0` returns 1), times the tone-map scale `+0x3a8c` |
| upload | `0x180025a9f`-`0x180025ad1` | `SetPixelShaderConstant( 29, …, 1 )` |

**What `+0x758` is.** The object at `this+0x388` gets vtable `0x18007cce8` in the constructor `FUN_180011f00`
(`0x180011f7f`/`0x180011f86`). Its slot `+0x758` (`0x18007d440`) is `0x18001aed0`, `MOVAPS XMM0, XMM1; JMP
0x18003ddf0`. `0x18003ddf0` is the exact sRGB decode: it clamps to 0..1, then `x ≤ 0.04045` (`0x180082924`) ? `x ·
0.0773994` (`0x180082930`, 1/12.92) : `pow( x · 0.947867 + 0.0521327, 2.4 )` (`0x18008294c` 1/1.055, `0x18008292c`
0.055/1.055, `0x180082988` 2.4).

**What `+0x2040` is.** It has one writer that sets it from an argument: `0x180019970`, vtable slot `+0x920` of the same
object (`0x18007d608`). It stores the bool, re-runs this upload (`0x1800199a1`), and when the bool is set and
`r_shader_srgb` is nonzero binds the shader-sRGB lookup texture to sampler 15 (`0x1800199bf`). That is the dynamic
sRGB-write state (*interpolated from what the function does*; no string names it). Every lit stock shader sets sRGB
write: `pShaderShadow->EnableSRGBWrite( true )` (`lightmappedgeneric_dx9_helper.cpp:614`). The modulating shaders do not
(B637).

**So, at TF2's defaults, a lit draw's fog colour is `SrgbDecode( gamma )` under range fog, regardless of `r_shader_srgb`,
and also under height fog, where only `r_shader_srgb 1` would give `pow 2.2`.** Then it is multiplied by the tone-map
scale.

## Ours

- Range fog: `FogConstants.For`, `managed/Tf2DemoSalvage.Render/FogConstants.cs:43-45`, `MathF.Pow( c, 2.2 )`. Its
  remark (`:14-17`) says the curve was *interpolated* as mathlib's `GammaToLinearFullRange`. The disassembly above
  replaces that.
- Height fog: the water volume's colour, `WorldRenderer.Water.cs:481-483`, through `Linear` (`:490`), `pow 2.2`.
- The shader then lerps `lit` toward `fogColour.rgb · scale` by the factor squared (`WorldRenderer.cs:946`), or toward
  `heightFogColour.rgb` by the water factor (`:2066`), in linear light, and the target's sRGB view encodes the result.
  This part already matches.

**Not in this change** (other paths that only look the same):
- `Linear` on the water material's `$refracttint`, `$reflecttint` and `$fogcolor` (`WorldRenderer.Water.cs:543-548`,
  `:960-962`, `DetailSpriteRenderer.cs:360-362`). These go through stdshader's `SetPixelShaderConstantGammaToLinear`,
  not this upload, and were not read here. They keep `pow 2.2`, so `Linear` stays for them.
- The water view's clear colour (`WorldRenderer.Water.cs:761-763`), from `SetClearColorToFogColor` in the engine
  (`viewrender.cpp:759-766`). It does not go through c29.

## The size of the change (*arithmetic*)

Per channel at FULL fog, where the pixel is the fog colour, stored through the sRGB target. "pow" is ours now, "sRGB"
is Valve's. Fog colours read from each map's `env_fog_controller` with the `entity-keys` probe, 2026-11-02.

| map, controller | colour | linear pow / sRGB | full-fog store pow / sRGB |
|---|---|---|---|
| `cp_process_f12` | 120 133 158 | 0.1905 / 0.1878, 0.2388 / 0.2346, 0.3489 / 0.3419 | 120.8 / 120, 134.1 / 133, 159.4 / 158 |
| `plr_hightower` | 181 175 170 | 0.4704 / 0.4621, 0.4368 / 0.4287, 0.4098 / 0.4020 | 182.5 / 181, 176.5 / 175, 171.5 / 170 |
| `ctf_2fort`, `fog_controller` | 130 139 170 | 0.2271 / 0.2232, 0.2632 / 0.2582, 0.4098 / 0.4020 | 131.0 / 130, 140.2 / 139, 171.5 / 170 |
| `ctf_2fort`, `fog_blue_spytech` (darkest of its six) | 58 75 82 | 0.0385 / 0.0423, 0.0677 / 0.0704, 0.0824 / 0.0844 | 55.2 / 58, 73.6 / 75, 81.1 / 82 |
| `koth_viaduct`, `fogcolor` | 213 174 221 | 0.6730 / 0.6654, 0.4313 / 0.4233, 0.7299 / 0.7231 | 214.1 / 213, 175.5 / 174, 221.9 / 221 |
| `koth_viaduct`, `fogcolor2` (blend) | 191 170 215 | 0.5295 / 0.5210, 0.4098 / 0.4020, 0.6870 / 0.6795 | 192.4 / 191, 171.5 / 170, 216.0 / 215 |
| `pl_upward` | 174 193 205 | 0.4313 / 0.4233, 0.5418 / 0.5333, 0.6187 / 0.6105 | 175.5 / 174, 194.4 / 193, 206.2 / 205 |

With the sRGB decode, full fog stores the controller's own bytes exactly, since the target encodes by the inverse curve.
`pow 2.2` lands about 1 to 1.5 levels HIGH on every outdoor colour above, and LOW on dark ones: 2.8 levels low at 58,
6.85 at 26, 7.35 at 10, and the most is 8.54 at 16 (8.3-8.5 across 13-18; review). The highest it lands is 1.49
at 171. The two curves cross near 0.5 (128 stores 129.0 against 128).

**On a typical frame** the fog factor is well under one, and the change is the factor squared times the linear
difference: at f² = 0.25 on `cp_process_f12`, under half a level. **So the expected pixel change on an outdoor fogged
frame is 0 to 1 level, mostly 0, and up to the full-fog number only at the far end of the fog.** Interiors with dark
fog, such as 2fort's spytech, change most. Water fog colours are darker still, so the height branch moves more there.

## The test (conformance first, red for the right reason)

`FogColourCurveConformanceTests` in `Rendering.Tests`, with a hand-built lit material (`MapAssets.ForMaterials`, no
install). The fog colour is DARK, 26 on one channel and 58 on another, where the curves differ by 7 and 3 levels. A
mid-grey would be under the test's resolution.
- Range fog, full density: the pixel is the fog colour's own bytes (26, 58, 128). With `pow 2.2`: 19, 55, 129.
- Range fog, half (factor² = 0.5): `encode( 0.5 · decode( unfogged ) + 0.5 · SrgbDecode( fog ) )`, ±1.
- Height fog, half (factor 0.5, unsquared): the same formula with the volume's colour.
- Control: the unfogged draw differs from every fogged one.
- `FogConstantsTests` gets its expected colour from the sRGB decode.

Sabotage: put `pow 2.2` back in each of the two sites in turn. Each must redden its own tests only.

## Implementation

One `SrgbDecode` beside `FogConstants`, used by `FogConstants.For` and the water volume colour. `Linear` stays for the
material-parameter conversions above.

## Results (2026-11-01)

**Tests.** `FogColourCurveConformanceTests` (5) and the rewritten `FogConstantsTests` case were red before the fix, all
through `OffscreenTarget.DrawWorld`, the production world shader. Full fog stored (3, 19, 55) for fog bytes
(10, 26, 58). Half fog stored (1, 11, 38) against Valve's (5, 16, 40). Both sets are the `pow 2.2` answers. Green
after the fix. Sabotage with `pow 2.2` put back in one site at a time:
- in `FogConstants.For`, the two range cases and `FogConstantsTests` reddened (3);
- in the water volume colour, the two height cases reddened (2).

Each site was restored by inverse edit.

**Counts after the fix:**

| assembly | passed | skipped or not run | failed |
|---|---|---|---|
| Rendering | 1307 | 78 of 1385 not run | 0 |
| Presentation | 1330 | 0 | 0 |
| Scene | 3430 | 0 | 0 |
| Content | 1828 | 29 | 0 |
| Animation | 5386 | 1 | 0 |

Whole `Tf2DemoSalvage.slnx` build: 0 warnings, 0 errors.

**The real frame.** f12 (`D:/measure-f12/f12.dem`, cp_process_f12, fog 120 133 158, range 100..11000), tick 30000,
Debug `--shot`, 984x551. The "before" is the same tree with `pow 2.2` put back in both sites, built and shot, then
restored. Diffs are from `D:/measure-f12/b653/scripts/diff.py`. Images are under `D:/measure-f12/b653/` (`before/`,
`after/`, `after2/`, `diffmap-a.png`, `diffmap-c.png`).

| camera (`TF2VIEW_CAMERA`) | px changed | deltas, after minus before | sign |
|---|---|---|---|
| a `-3955 -954 640 12 157` (entity 8's framing, layer-order plan) | 1,142 | −1 on 1,147 channels | every channel down |
| b `-2240 -3180 790 5 90` (finding 80's door camera) | 3,981 | −1 on 4,018, −2 on 7 | every channel down |
| c `-3280 -1152 637 15 225` (finding 82), against the repeat `after2/c.png` | 13,333 | −1 on 13,524, −2 on 48, −3 on 1 | every channel down |

**Control:** the first `after/c.png` against its own repeat `after2/c.png` (same build) differs on 64,585 px by up to
181 levels. So that first shot was not repeatable, and it is not used: something besides the fog moved in it. The
repeat was taken under the lock, as every shot was. Shots a and b have no repeat. Their deltas are all −1 or −2 and
never positive, which is the predicted signature, not noise.

**Do not pair** `after/c.png` (the unrepeatable shot) or `after/d.png` / `after/e.png` with anything. d and e have
no before shot: that run was stopped at its time limit.

**As predicted:** every change is DOWN, at most 1-3 levels, and it sits on the scattered pixels that cross a rounding
boundary. They are densest in the far room (`diffmap-c.png`). At f12's colour `pow 2.2` stored about 1 to 1.4 levels
high at full fog, and these distances reach only a fraction of full fog. **The change is invisible on this frame. It is
parity, held by the synthetic tests.**

**The fog-heavy frame.** f12's cameras reach only a fraction of full fog, so one outdoor frame on a map with nearer fog
was shot as well. **The demo changes here, to `demostf-pl_upward_f12-1491354.dem`** (lcor). Its map is
`pl_upward_f12`. Read with `entity-keys`: the world controller is 174 193 205, 100..6000, max density 0.85; the
`sky_camera` is 174 193 205, 100..20000, scale 16. Settings: `--spectate 3 --tick 20000 --first-person`, so the camera is
player 3's recorded eye (`831.4 570.3 456`, pitch 10.2, yaw 47.2, from the shot's sidecar). The tone-map scale is
0.800975 in all four shots' logs.
- **Repeat controls:** `after1` against `after2` differ on 0 px. `before1` against `before2` differ on 21,372 px, all
  in rows 0-36, which is the HUD bar. The scene rows agree. So `before2` is paired with `after1`.
- **Before (`pow 2.2`) against after: 35,913 px changed, every one DOWN.** Box (0,37)-(973,432), the scene and not the
  HUD. Channel deltas: −1 on 46,277, −2 on 8,710, −3 on 816, −4 on 20, −5 on 1.
- `upward/diffmap.png` shows where: on the far snow mountain (3D sky) and the distant buildings, which are the
  most-fogged pixels. The near wood and the viewmodel are untouched.
- **Largest single change:** (389,164) on the mountain, (176,171,162) to (175,169,157).

**Against the prediction:** the sign and the place are as predicted. **The size is not.** For the controller's colour
at scale 0.80, full fog moves a stored channel by at most about 1.2 levels (*arithmetic*). Up to 5 were measured on the
3D-sky mountain. Not explained here. The 3D sky's fog colour reaches the view from the player's `m_skybox3d.fog`, which
was not read for this tick (`moment-dump` does not print it). A darker colour there would give bigger deltas, as the
table above shows. The post chain is the other candidate. *Open.*

## Review batch (branch `fix/fog-colour-curve-review`, from `38a984c5`)

**1. The 5 levels are not exposure feedback (measured, refuted).** I shot the same camera with the exposure pinned at
the settled value, `+mat_force_tonemap_scale 0.800975`. Each side was built from the review tree; the before side had
`pow 2.2` put back in both sites and then restored. Images: `D:/measure-f12/b653/pinned/`.

| pair | px changed | deltas | max |
|---|---|---|---|
| `before2` against `after`, pinned | 36,089 | −1 on 46,503, −2 on 8,771, −3 on 793, −4 on 20, −5 on 1; none up | 5, at (389,164) as before |
| `before-nobloom` against `after-nobloom`, pinned, `+mat_disable_bloom 1` | 29,860 | −1 on 40,888, −2 on 3,083, −3 on 79, −4 on 4; none up | 4 |

- The repeat `before1` against `before2` differs only in the HUD rows 0-36.
- The auto-exposure shots already logged 0.800975 on both sides, and the pinned and unpinned after shots differ by at
  most 2 levels. So the exposure never moved.
- Bloom carries about one level of it.
- The rest are isolated single channels on the mountain, e.g. (390,165) green 149 to 145 with red and blue unchanged.
  A shader that lerps toward a colour cannot do that. A `$mod2x` overlay over the fogged surface would: it doubles the
  destination's change. That is the leading candidate, *interpolated, not checked*.
- (The earlier note that a darker 3D-sky fog colour would give bigger deltas had the sign wrong. Below about byte 128
  the decode is BRIGHTER than `pow 2.2`, so such pixels would have gone up, and none did.)

**2. Refract fog colour.** Refract's shadow state is `DefaultFog()` with `EnableSRGBWrite( true )`
(`refract_dx9_helper.cpp:155`, `:213`), with no `DisableFogGammaCorrection`. So c29 arrives times the tone-map scale.
Both of our refract fogs lerped toward the unscaled colour. Now:
- the refract model multiplies by `debugModes2.y`, as `PixelFog` does;
- the refract sprite folds the scale into its constants (`SetRefract`, `Device3D` passes `LinearLightScale`).

`RefractModelRenderTests.DrawModelPose_TheLensInFullFogAtHalfToneMapScale_IsTheScaledFogColour` and
`RefractTrailRenderTests.Render_ARefractStripInFullFogAtHalfToneMapScale_IsTheScaledFogColour` were red first (255
against 188). `OffscreenTarget.DrawModelPose` takes a fog now, so the model test can set one.

**3. `LinearGrey`** is written into the shader from `FogConstants.SrgbDecode( 128/255 )` when the source is assembled.
`FogConstantsTests.ShaderSource_TheHeightFogGrey_IsTheSrgbDecodeOf128` was red against the hand copy.

**4. Water.**
- (a) Read: `water.cpp` sends its tints and fog colour through `CBaseVSShader::SetPixelShaderConstantGammaToLinear`
  (`BaseVSShader.cpp:146-165`; `water.cpp:291`, `:311`, `:342`). That is `x > 1 ? x : GammaToLinear( x )`, mathlib's
  table of `pow( round( x · 255 ) / 255, 2.2 )` with 1 from 0.95 (`color_conversion.cpp:210`, `:274-293`). The
  reflect tint under integer HDR is `GammaToLinear( x ) · 4`, with no pass-through (`:296-307`). `Linear` is now that
  (`ModelMaterialShading.ModulationToLinear`), and the reflect tint has its own branch.
  `WaterColourConformanceTests` was red first on 0.5, 0.97 and 1.5. The water surface's `pow 2.2` fog beside the
  height fog's sRGB decode is Valve's own seam, kept.
- (b) The clear colour: `SetClearColorToFogColor` takes the byte times `pow( scale, 1/2.2 )`, truncated into an
  `unsigned char` (`viewrender.cpp:754-768`). Ours now clears the sRGB view with `SrgbDecode` of that byte, so the
  stored byte is the engine's: 200 at 0.5 stores 145, red first. **Open:** D3D9 honours `D3DRS_SRGBWRITEENABLE` on a
  clear ("Gamma (Direct3D 9)", Microsoft Learn). If shaderapi clears with sRGB writes on, the bytes are taken as linear
  and stored brighter. Which state is current at the clear was not read.

**5. The decode, re-read.** `0x18003ddf0`'s constants are the textbook curve, with no table: 0.04045 (`0x3D25AEE6`),
1/12.92 (`0x3D9E8391`), 1/1.055 (`0x3F72A76F`), 0.055/1.055 (`0x3D55891A`), 2.4 (a double). `SrgbDecode` now uses
them bit for bit and takes the pow in double, as the binary does.
`FogConstantsTests.SrgbDecode_BetweenTheTwoPublishedKnees_TakesTheLinearBranch` feeds it 0.04, between 0.03928 and
0.04045. The two branches differ there by 4.8e-7, which the test proves exceeds its tolerance first. Sabotage: knee
0.03928 reddens it. The clamp is the binary's (MAXSS/MINSS) and is tested at −0.5 and 1.5. No test here needs a device.
**Not read:** the LDR side of the upload and the HW sRGB gate. **Not modelled:** `r_shader_srgb 1`. Both are in the
`FogConstants` remark.

**6. Docs and tests.**
- The largest gap is 8.54 levels low at byte 16, not 7.
- The half-fog blue case keeps ±1: its `pow 2.2` answer, 38, lies outside 39..41, and the separation check asserts it.
- The height cases assert the identity camera has no eye (`EyePosition.From`), the premise their saturated fraction
  rests on.

**Counts (review):** Rendering 1324 passed, 59 skipped, of 1383. Presentation 1331. Scene 3451. Content 1828 (29
skipped). Animation 5386 (1 skipped). None failed. Whole `.slnx` build: 0 warnings, 0 errors.

## What is unsettled

- The isolated 3-4-level single-channel changes on `pl_upward_f12`'s mountain (`$mod2x` overlay suspected, not checked).
- Whether shaderapi's clear runs with sRGB writes on (the water clear colour, above).

- The range branch's gate (hardware-config `+0x1c0`, the ConVar at `0x1800a2378`) is unidentified. If it were false,
  c29.rgb would be zero. TF2 draws coloured fog, so it holds on a live client (*interpolated*).
- `+0x2040` as "sRGB write" is read from what its setter does, not from a name.
- No TF2 capture against ours. The difference is at most a level or two here, below what a live pair could settle.
- A fogged frame with height fog was not shot (no water in these cameras). The height path is held by the synthetic
  test only.
- No real frame with water was shot for the water changes; they are held by the unit tests only.
