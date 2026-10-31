# Measurement 2026-10-31: explosive scorch decals, dark and blocky at a close camera (B633)

**Status: RUN, verdict recorded.** Method and predictions were committed before the controlled shots (`ecc8977c`, and
predictions 5-7 before their runs); the exploratory shots under Observation were taken first and raised the question.

## Observation

The owner, looking at `D:/measure-f12/decal/close1.png` (f12, tick 30000, camera `-3516 -2200 763 35 -28`, 984x551, Debug
`--shot`), called the explosion scorches "massive pixelation" and "like our transparency or something is set to super low", and
beside it called the scorches in `D:/measure-f12/b624/merged-after-29900.png` (tick 29900, camera `-3722 -1403 726 25 99`, also a
Debug `--shot`) "much cleaner and less pixelated", and close1's "dark". His words in full: `docs/RISKS.md` B633.

Exploratory shots, taken before this file (images under `D:/measure-f12/decal-cmp/debug/`, all Debug, `--shot`, 984x551, close1's
camera, `TF2VIEW_WINDOW_POS=-3000,100`, build of `c01b388e`):

| shot | what changes | seen |
|---|---|---|
| `A-30000.png` | nothing: close1 again | the same dark smudged floor as close1, by eye identical |
| `B-30000-nodecals.png` | `+r_decals 0` | the dark haze is GONE; debris chunks and a faint smoke wisp remain |
| `C-30400.png` | 400 ticks later: the tick-29947/29972 explosions' particles have died, the decals persist | dark floor, no grain, but broken into straight-edged rectangles of different darkness |
| `D-30400-nodecals.png` | `+r_decals 0` at 30400 | smooth floor, no rectangles |
| `E-29940.png` | before the two nearest scorches land | lighter, older scorches only |

*Measured (picture), interpolation flagged:* the darkness is the decal layer, not smoke (A against B); the grain in A over it is the
explosions' debris and smoke particles, which the viewer cannot switch off (no particle cvar; the A/C time control stands in for
one). The two builds compared by the owner were both Debug (both sidecars say `"build": "Debug"`), so the build is not the variable.

**What the decal feed says is under the camera** (`world-decals f12 29900 30400`, the viewer's own `DemoTimeline.Decals`):
`scorch2_subrect` at (-3524, -2124, 576) tick 29947 and at (-3338, -2233, 576) tick 29972; then by 30400 two more within reach,
(-3284, -2299, 576) tick 30087 and (-3636, -2065, 576) tick 30143. 2,368 decal events in the demo (the control: the feed is not
empty).

**What the decal is** (read from the shipped VPK, `vmt`, `game-file` and `texel-png` probes): `decals/scorch1_subrect` and
`scorch2_subrect` are Subrects of `decals/decals_mod2x` (`$Pos` 512 256 / 768 256, `$Size` 256 256, `$decalscale` .65, so 166.4
units across). The atlas is `DecalModulate`, `$decal 1`, `$translucent 1`, base texture `decals/decals_mod2x.vtf`: VTF 7.4,
1024x512, DXT5, 11 mips, flags 0x2000 (EIGHTBITALPHA). Each scorch window is smooth (`scorch1-texels-4x.png`, nearest-neighbour);
its alpha is a hard cut-out mask (`scorch1-texels-4x.alpha.png`): 25,024 of 65,536 texels above zero, and the 40,512 at zero
average RGB 127.9 127.7 127.8 (range 117-131), so 1.00 under mod2x to within 1%.

## Question

What makes the floor under a fresh scorch draw as straight-edged blocks of different darkness in our frame, which layer and which
mechanism, and does real TF2 draw the same spot without them?

## Hypothesis

H1: the blocks are per-face or per-luxel structure leaking into the decal (a fragment missing on one face, a texture basis that
differs per face, or the decal picking up lighting it should not, since Valve's DecalModulate is unlit:
`STATIC_LIGHT_LIGHTMAP 0`, `DecalModulate_dx9.cpp:186`).
H0 (the competing reading): the blocks are the decal's own square quad, drawn because ours does not alpha-test DecalModulate where
Valve does (`EnableAlphaTest( true )`, `AlphaFunc( SHADER_ALPHAFUNC_GREATER, 0.0f )`, `DecalModulate_dx9.cpp:64-65`; ours keeps
`alphaTested` at 0 for a `$translucent` material, `WorldRenderer.cs:3495`).

## Prediction (fixed before the runs)

1. **Alpha test added in a scratch build** (clip at 0.5/255 for DecalModulate only): the rectangles at 30400 SURVIVE. Reason: the
   outside-mask texels are 128 within 1%, and the rectangles measure about 25-50 screen pixels (16-32 units at about 1.5 px a unit),
   where a 166-unit decal square would be about 250 px across.
2. **The pixels on either side of a rectangle edge** (`pixel-face`): two different faces.
3. **`+mat_fullbright 1`** (after confirming on D that the floor goes flat): the rectangles stay if the cause is placement or UV,
   go if the decal is lit.
4. **An isolated scorch** (no other scorch within 170 units up to the shot tick), camera 100-200 units off its centre, 300 ticks
   after it lands: the blocks appear on one scorch alone, so per-face or per-luxel, not an interaction between layers.

Falsified if: (1) the rectangles vanish with the alpha test; (2) both pixels are one face; (4) one scorch alone is smooth.

**Added after run 1 (the alpha test) falsified prediction 1, before the runs it predicts.** Run 1 showed the outside-mask region
darkens the floor although its texels are 128, so something multiplies the decal's output. Read in our shader: `PixelFog`
multiplies every material's colour by `LINEAR_LIGHT_SCALE` (`WorldRenderer.cs:882`), the HDR_TYPE_INTEGER tone-map scale,
including a modulating decal; Valve's DecalModulate writes `FinalOutput( result, fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_NONE )`
(`decalmodulate_ps2x.fxc:44`). Under mod2x a texel of 128 then multiplies the floor by the exposure rather than by one.

H2: the rectangles and the extra darkness are the tone-map scale applied to a DecalModulate draw.

5. **`+mat_force_tonemap_scale 1`** on the unchanged build at 30400: the rectangles VANISH (the scale is one, so 128 is neutral
   again) and the scorch interiors get lighter than in C.
6. **`+mat_hdr_level 0`** on the unchanged build at 30400: the same as 5 (`LinearLightScale` is 1 outside integer HDR,
   `Device3D.cs:1652`).
7. **The fix, applied in a scratch build** (no tone-map scale on a DecalModulate draw, nothing else changed) at 30400 with
   default settings: no rectangles; the alpha test is a separate divergence and stays out of that build.

Falsified if 5 or 6 still shows straight-edged blocks.

## Method

Every shot: `tf2demoview D:/measure-f12/f12.dem --tick <t> --shot <png>` with `TF2VIEW_CAMERA` as stated and
`TF2VIEW_WINDOW_POS=-3000,100` (off screen, as the earlier shots), under `run-exclusive.ps1`. One variable per shot. Crops with
`texel-png crop <png> <out> 230 230 240 180 4` (nearest-neighbour, 4x, the same rectangle in every frame). Scratch builds go to
`D:/measure-f12/decal-cmp/scratch/bin-<variant>` from the worktree with the one change, reverted by an inverse edit afterwards.

Real TF2 is the tie-break, not the first measurement any more (the owner moved the question to our frames): in demo playback the
roaming camera's PITCH cannot be set from the console (`spec_goto` is server-side, `player.cpp:6692`; `+lookdown` only acts when
`cl_mouselook` is 0, `in_main.cpp:711`, and `cl_mouselook` refuses to change while playing a demo, measured this session). So a TF2
frame at a stated camera is an in-eye camera read back with `spec_pos`, copied into ours at TF2's `demoui` tick, with
`+mat_hdr_level 0` on our side to match the owner's client.

## Control

- A against close1: the same frame reproduced, so the shot path is stable before anything is varied.
- Each `+r_decals 0` frame is the decal-less control for its decal frame.
- `pixel-face` is asked first for a pixel that must be the floor.
- `+mat_fullbright 1` is checked on a decal-less frame first: the floor must go flat, or the switch is not reaching the draw.

## Environment

- Machine: RTX 3060, i5-10600K, desktop 1920x1080. Viewer windows placed off screen.
- Builds: Debug from `c01b388e` (the exploratory shots), scratch variants named per shot.
- TF2 (shipped client, through the tf2 MCP, 1280x720) read back: `mat_hdr_level 0`, `mat_picmip -1`, `mat_forceaniso 16`,
  `mat_trilinear 1`, `mat_antialias 1`, `mat_colorcorrection 0`, `mat_bumpmap 1`, `mat_specular 1`, `mat_fullbright 0`,
  `r_decals 2048`, `mp_decals 2048`, `r_decal_cullsize 1`, `fov_desired 75`, `spec_autodirector 1`, `mat_dxlevel 95`.

## Result

Images under `D:/measure-f12/decal-cmp/`; crops are the same 240x180 rectangle at (230, 230), 4x nearest-neighbour.

| run | build / switch | tick | seen (picture) | images |
|---|---|---|---|---|
| 1 | scratch: DecalModulate alpha-tested at 0.5/255, nothing else | 30400, 30000 | rectangles GONE; each scorch's cut-out outline shows | `alphatest/`, `crop-alphatest-30400-4x.png` |
| 5 | unchanged, `+mat_force_tonemap_scale 1` | 30400 | rectangles GONE; soft scorch like the shipped texels | `debug/F-30400-tonemap1.png`, `crop-F-30400-tonemap1-4x.png` |
| 6 | unchanged, `+mat_hdr_level 0` | 30400 | the same as 5 | `debug/G-30400-ldr.png`, `crop-G-30400-ldr-4x.png` |
| 5b | unchanged, `+mat_force_tonemap_scale 1` | 30000 (the owner's frame) | no dark haze; looks like the clean 29900 shot; the whole frame brighter, so the automatic exposure there was below one | `debug/H-30000-tonemap1.png` |
| 7 | the fix (no tone-map scale on a modulating draw), default settings | 30000, 30400, 29900 | no squares, no haze, soft scorches | `fix/`, `crop-fix-*-4x.png` |

**Before against after, whole frame** (`pair-diff`, the viewer's own captures): 30400 differs in 210,577 of 542,184 px, max
channel step 52, box (0,37)-(690,550), and the difference image (`sbs-before-after-30400.png`, right panel) is
exactly the decal squares on the floor and nothing else. 30000 differs in 237,410 px, max 61, over the whole frame; the
**control**, the same build shot twice at that frame (`close1.png` against `A-30000.png`), differs in 27,120 px, max 61, box
(0,0)-(983,292): live particles. Side-by-sides: `sbs-before-after-30000.png`, `sbs-before-after-30400.png`,
`sbs-fix-vs-tonemap1-30400.png`.

**The conformance test, red then green** (`DecalModulateConformanceTests`, the shipped atlas sampled at one texel over a
cleared 188-grey surface): before the fix a neutral texel moved the surface to 139 at scale 0.5 and to 255 at scale 2, and a
scorch centre drew 100 at scale 1 against 71 at scale 0.5; after it, 3 of 3 pass. The control in it (a scorch centre must
darken the surface by more than 40) first caught the test drawing NOTHING (a world pass with no runs returns before its
decals) and the neutral cases passing vacuously; fixed in the test before the red run was counted.

**Gate phase 1 on the fix** (gcor): core 2737, cli 75, logging 37, fonts 5, animation 5368, scene 3417, audio 383,
presentation 1330, content 1890, corpus 242, rendering 1312, viewer 159 executed, 0 failed, every one at or over its floor.

**Not run:** predictions 2 (`pixel-face` at an edge), 3 (`+mat_fullbright 1`) and 4 (an isolated scorch). They were
written to separate per-face from per-luxel causes; run 1 then pointed at a multiplier on the output and runs 5-7 settled
it, so they no longer answer anything. **Real TF2 at the same camera was not captured**: the demo's roaming camera cannot
take a pitch from the console (Method), and the owner moved the question to our own frames before an in-eye pairing was
made. **The live window was not captured** either; the exposure explanation predicts that a live view differs from a
`--shot` wherever its exposure differs, which is *interpolated, not measured*.

### Every divergence found, ranked by what it does to this picture

1. **The tone-map scale reaches a modulating draw** — `PixelFog` scaled every output by `LINEAR_LIGHT_SCALE`; Valve's
   `Modulate`/`DecalModulate` are `TONEMAP_SCALE_NONE` (`modulate_ps2x.fxc:37`, `decalmodulate_ps2x.fxc:44`,
   `common_ps_fxc.h:356-358`). *Measured* as the cause (runs 5-7). **Fixed** on `fix/b633-modulate-tonemap-none`.
2. **No alpha test on DecalModulate** — Valve `EnableAlphaTest( true )`, `AlphaFunc( GREATER, 0 )`
   (`DecalModulate_dx9.cpp:64-65`); ours tests only `$alphatest` materials (`WorldRenderer.cs:3495`) and `decals_mod2x` is
   `$translucent`. After the fix the untested region is the 40,512 outside-mask texels at 128 ± 11 (*measured*), under 1%
   on the mean; DXT noise down to 117 can still darken a texel by about 8%. *Read*; its visible effect after fix 1 is
   *not measured*. Open.
3. **Fog** — Valve fogs a DecalModulate draw toward middle grey with `pow( saturate( fogFactor ), 0.4 )`
   (`decalmodulate_ps2x.fxc:42`, `FogToGrey`, `DecalModulate_dx9.cpp:80`); ours applies none (already marked `ponytail:`
   at `WorldRenderer.cs` fogMode). No fog in this room, so nothing here. *Read*. Open.
4. **Gamma-space mod2x reproduced in linear space** — `ModulateTwiceLight` (`WorldRenderer.cs`, 2^2.2/4) is the 2.2
   approximation of the sRGB curve: a 128 texel gives 0.992 against Valve's 1.004. *Arithmetic*. Known, documented there.

Checked and NOT divergent: the decal is unlit as Valve's (`DecalMesh` binds the atlas's white texel); the sampler is
16x anisotropic as the owner's `mat_forceaniso 16`; the scorch material is the atlas Subrect at `$decalscale` .65 (166.4
units) as shipped. Not checked: the texture size the viewer uploaded for the atlas (the fixed shots show no blockiness,
so not pursued).

## Verdict

**H2 confirmed (predictions 5, 6, 7 confirmed; prediction 1 falsified; 2-4 not run, inconclusive):** the dark,
straight-edged "pixelation" under f12's scorches was our shader scaling a `DecalModulate` draw by the HDR exposure, so
every scorch's neutral 128-grey surround darkened the floor across its whole 166-unit square whenever the exposure sat
below one. Valve's shader takes no tone-map scale. The fix is one condition in `PixelFog`, held by a red-first render
test; the after-shot at the owner's camera draws soft scorches with no squares. Because the exposure follows the view,
the same decals looked clean from the tick-29900 camera, which is why build, capture path and magnification were all
suspected first. **Shots affected:** every viewer capture on an HDR_TYPE_INTEGER map under automatic exposure with a
decal or `Modulate` material in frame, including the B624 and B622 before/after shots (both sides drew the same fault, so
their between-build differences stand, and the decals in them are too dark); the golden comparisons pinned at
`mat_force_tonemap_scale 1` are not affected (a scale of one is the identity), while their auto-exposure cases are, in
proportion to the decal area in frame.
