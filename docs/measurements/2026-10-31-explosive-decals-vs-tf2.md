# Measurement 2026-10-31: explosive scorch decals, dark and blocky at a close camera (B633)

**Status: method and predictions written before the controlled shots.** The exploratory shots under Observation were taken
first and raised the question; they are kept as they were seen.

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

## Verdict
