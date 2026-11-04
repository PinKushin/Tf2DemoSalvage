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

## Verdict

1. **Held.** The trace list reads 83 first, and 83 admitted, none on one side only. `Instances_CpProcessF12LowerPipe_HoldsTheEnginesSlotsAndCube` passes.
2. **Held, better than predicted.** Far camera, no shift, TF2 on Vulkan against ours: patch 241.7/241.1, upper pipe top 358.1/351.4, mid 303.8/297.0, lower top 358.4/352.1, mid 289.0/284.9, wall 207.7/199.0, gap 139.5/139.3, cap disc 194.2/192.0, left pipe upper 67.5/73.5, lower 93.2/100.7. Concrete 187.0/187.0 is the control. Unchanged to the digit from the pre-review figures.
3. **Held.** Two shots of one build: 0 pixels differ. `BrushModels.Append` with `surface.Normal` and no frame, temporary and reversed: 0 pixels differ at the far camera. Read: `ModelInstance.Light` is null for a brush entity, so `ambientCube[0].w` is 0 and `WorldRenderer`'s cube branch does not run. Counted: 17 materials on f12's brush entities, none with an `$envmap`, with the instrument's control 42 of 189 materials on the map having one. So nothing can read those normals here, and **the door camera the method planned was not taken**: that is a deviation, on that reasoning. On a map whose brush entity has an `$envmap` the normal does reach it, by the engine's own builder, which was not measured.

**After `Frame`'s fallback moved to the plane (B687):** those shots predate it, and it is what overlays and displacements reach. A shot at that source against `rev-far`: 0 pixels differ, so every box above stands for it too. That says nothing at a camera on a flipped overlay with an `$envmap`; none was sought.

**Not predicted.** That count's `1 − dot` reached 2 on half the corners: `BspSurface.Normal` opposes the compiled normal on a flipped face. Across the map, 26,602 corners of flipped faces side with the plane, 6 against, 174 across; 39,035, 6 and 203 on the others. B687.
