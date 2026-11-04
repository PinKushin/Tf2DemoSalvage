# B669's last two items: what darkens the cavity's world in TF2, and the pipes' bottom-heavy shortfall

## Observation

`measurements/2026-11-03-b669-the-pane.md`, by this project against live TF2 11191711. Far camera `-3722 -1403 726 25 99`, glass hidden: wall 126.5 in TF2 against 139.8 in ours, gap 24.4 against 41.1, while ours equals the lightmap lump row by row. Close camera `-3390 -700 540 0 90`: the world agrees to about 7%. Pipes, close camera, lighting only, TF2/ours: 116.5/108.4, 86.9/71.7, 64.6/47.5.

## Question

1. What multiplies TF2's world below the lump, more at the far camera than close up? A decal, fog, or the LOD of a layer?
2. Which of the pipe placement's cube faces and lamps differ from TF2's, and why?

## Method

Read before measuring: the entity lump for `infodecal`, `info_overlay` and fog near (-3390, -640, 520); each cavity material's layers and their mip chains (`vtf`); how ours uploads and samples each layer. In TF2, one listen server as in the record above, cvars read before and read back after, including `cl_drawhud` and `r_drawviewmodel`: `r_cleardecals permanent`, fog off, and a layer isolated at a time, far and close. In ours, `--shot` at the same cameras under `run-exclusive.ps1`, with temporary edits reversed where a switch does not reach a shot (B671).

Pipes: `prop-shade` prints all six cube faces and each lamp the draw was given, carried from the values the code used. TF2's are read by engine disassembly and by isolation: `r_worldlights 0` empties the slots into the cube, `mat_fullbright 2`, and `r_lightinterp` is not reached by a static prop (B674).

Numbers are mean linear luminance x1000 by the boxes in `D:/measure-f12/b669/rem/boxes.py`.

## Control

TF2's reference patch at 228.1 before any manipulation; concrete at 187.0 under every lit switch. A switch that leaves a shot byte-identical did not reach the draw (B671). The six-face print must show the up face the old print showed, (0.375, 0.370, 0.349) on the first placement.

## Environment

Machine as in the record above. TF2 build from `version`. Ours: branch head at the run, in each shot's `.json`.

## Result

Raw shots: `D:/measure-f12/b669/rem/tf2/` and `ours/`. Build 11191711. Control: concrete 187.0 under every lit switch, on D3D9 and on Vulkan. `cl_drawhud` and `r_drawviewmodel` read 1 and 1 on two fresh launches, neither is in `config.cfg`, and both read back 1 after. `mat_hdr_level` read back 0.

**The wall, on one surface, along one ray** (aimed at the screen centre, footprint held constant; lit, glass hidden, D3D9): 42.8 at 40 units, 41.6 at 100, 37.0 at 215, 32.7 at 430, 27.6 at 859. **Far camera, column x 930-960, D3D9 with props, D3D9 without (`r_drawstaticprops 0`):** equal to the digit down to row 265, then 129.2/131.0, 128.7/134.0, 126.6/135.3, 123.1/134.5, 116.4/130.8, 83.0/96.7, 63.7/76.5, 55.0/71.4, 49.6/70.3 at the pipe's edge; below it 25.5/39.5, 24.2/34.5, 21.9/27.6, and equal again by row 325. At 215 units the halo is again about 11 rows (211-221: 79.5/81.5 to 54.9/80.1), so its width is in screen pixels, not world units. No static prop lies there in `r_drawstaticprops 2`.

**Switches that left it:** `r_shadows 0`, `r_renderoverlayfragment 0`, `r_shadowrendertotexture 0` (all to the digit), `r_lod 0`, `mat_forceaniso 1`, `mat_picmip 0`, `mat_fullbright 2`. `find ssao`, `find occlusion` and the shader DLL's strings name no such effect.

**`-vulkan` removes it:**

| box | TF2 D3D9 | TF2 Vulkan | Vulkan, bicubic off | ours |
|---|---|---|---|---|
| patch, glass hidden | 178.8 | 196.2 | 196.6 | 177.9 |
| upper pipe top / mid / low | 354.7 / 281.0 / 232.1 | 356.2 / 280.9 / 233.4 | same | 311.4 / 233.0 / 166.9 |
| wall behind | 126.5 | 153.5 | 150.4 | 139.8 |
| gap below | 24.4 | 43.9 | 41.6 | 41.1 |
| left pipe upper / lower | 54.3 / 83.0 | 67.5 / 93.2 | 67.1 / 93.1 | 63.0 / 82.0 |
| whole frame | 152.7 | 157.6 | 157.9 | 151.4 |
| concrete | 187.0 | 187.0 | 187.0 | 187.0 |

With the glass shown, Vulkan: patch 241.7, wall 207.7, gap 139.5, whole frame 160.0; ours 229.1, 199.0, 139.3, 154.0. Close camera `-3390 -700 540 0 90`: Vulkan and D3D9 equal on the pipe and the upper wall, 50.6, 138.9; ours 49.8, 136.4.

**What is left of the wall:** with `detailMap` sampled one mip coarser (a temporary edit, reversed), ours reads wall 151.6, floor 94.9, back wall 72.3, against Vulkan's bicubic-off 150.4, 93.8, 79.2; unedited 139.8, 90.7, 69.0. `overlays/detail001`'s mean rises from 125.5 at 128x128 to 136.0, 146.8, 155.7, 164.0 at the mips below it.

**The pipe:** `prop-shade` for #215, lit at (-3459 -683 506), carried from the instance: +x (0.123 0.116 0.106) -x (0.155 0.152 0.143) +y (0.071 0.066 0.057) -y (0.208 0.204 0.198) +z (0.272 0.272 0.283) -z (0.043 0.039 0.033); four lamps. `cube-rays` at that point: bounce alone -y (0.120 0.115 0.108), +z (0.153 0.152 0.163), -z (0.043 0.039 0.033). Reflectivity from the map equals each VTF header's on every material struck but one (`CONCRETE/CONCRETEWALL012Z`, 5%, on one ray).

TF2, lighting only, `r_worldlights 0`, the lower pipe's middle row, facing -y: 68.3 with `r_radiosity 4`, 43.4 with `r_radiosity 0` on a reloaded map; dividing by the grey albedo 0.2158 gives bounce 0.115 (ours 0.1147, to three digits) and lights 0.201. Ours' lights in that direction, the cube less the bounce plus the four lamps' share by hand: about 0.14. Facing -z: TF2 9.3, 0.043; ours 0.039. Ours with the trace disabled: -y 0.415, far past it.

## Verdict

Item 3: the far-camera darkening is a screen-space halo around geometry in TF2's D3D9 path only, gone under `-vulkan`, and is not in ours and not a parity target of the engine. *Interpolated:* a driver ambient occlusion, since no cvar or shader names it; the owner's driver setting was not read. What remains against Vulkan is the detail layer, about one mip brighter in TF2 at distance, cause not found (B680). Item 2: the bounce is exact; the shortfall is in the direct lights, about 0.06 on -y, and which light was not found.
