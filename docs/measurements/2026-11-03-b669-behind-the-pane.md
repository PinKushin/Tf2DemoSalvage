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

## Verdict
