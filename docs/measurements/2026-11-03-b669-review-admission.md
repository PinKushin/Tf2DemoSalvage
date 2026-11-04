# B669's review: does the corrected admission still hold, and what do a brush entity's normals move

## Observation

A review of B669's fix named the computed radius as reaching only one test. The load now writes it once, `Distance` no longer cuts a point or spot at it (flags 2), a handle's list tests the prop's box, and a zero-attenuation lamp is quadratic. And `BrushModels` gave a brush entity's corners the compiled normal in place of the plane's.

## Question

1. Do #215's slots, folds and admitted set still equal what the running engine logged?
2. Does the far camera's pipe, patch and frame stay at the fix's figures against TF2 on Vulkan?
3. Does a brush entity's vertex normal reach its ambient shading, and how much of a frame does it move?

## Method

1. `prop-shade` on #215, with `admit.py` against the logged trace list; `Instances_CpProcessF12LowerPipe_HoldsTheEnginesSlotsAndCube`.
2. `shot.ps1` far camera, `shiftbox.py` against `tf2/vk-ref`, boxes as in `2026-11-03-b669-pipe-traces-live.md`.
3. Read the pixel shader for what reads `shadingNormal`, and whether a brush entity's draw enters that branch. A/B: the head, then `BrushModels.Append` with the plane's normal, a temporary edit reversed afterwards, the same camera at a door of cp_process_f12 and the far camera. Counted: pixels that differ, largest channel.

## Control

Question 1: the trace list's own count, 83, before anything is compared; a light the map must hold is present in the listing. Question 2: the concrete box at 187.0 and `industrial_pipe01` at its recorded value. Question 3: two shots of one build first, as B677 says a byte comparison needs (rows of the HUD band aside; `cl_drawhud 0` here).

## Prediction

1. 83 admitted equals 83 traced, unchanged: no light at #215 has radius-cut folds, as every one is inside its cell test.
2. Within 0.5 of the fix's figures: patch 241.1, upper pipe top 351.4.
3. Ambient is not reached: a brush entity's `Light` is null and the cube branch is skipped. What moves is reflection on a smoothed envmapped brush-entity face; few pixels or none.

## Environment

Machine and build as in the records above. Ours: this branch's working tree at the run, Debug.
