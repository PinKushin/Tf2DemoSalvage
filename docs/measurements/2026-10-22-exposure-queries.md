# B575: the exposure histogram counted by occlusion queries instead of the processor

## Observation

Finding 92 (a sampled trace of the render thread on cp_process_f12, first person): `ExposureHistogram.TryRead` is 3.9% of
the render thread as a leaf. It walked one pixel in sixteen of the exposure region of a read-back copy, every frame.
`viewpostprocess.cpp:532-613` (read from published source) counts with occlusion queries, one entry a frame, and its
processor does no per-pixel work. Commit `5359aba8` replaces the shape (B575).

## Question

1. What does the frame cost on f12, before (`605e3785`) and after (`5359aba8`)?
2. Is the exposure the same in effect: the tone-map scale each frame was drawn with, sampled along a fixed-step playback, and
   the pictures at the same ticks?

## Hypothesis

The frame gets cheaper by roughly the leaf's share (3.9% of the render thread). The scale differs only where the engine's
shape differs on purpose: the new histogram's bins are three to nineteen frames old and each from a different frame, and its
goal history is pushed every frame rather than per reading, so a change of scene is followed more slowly.

## Prediction

- Steady mean frame time after is lower than before; falsified if after is not below before in both pairs.
- On stretches where the old path's own scale is steady, new and old agree within the control's spread (old against old);
  falsified if more than a fifth of the steady samples fall outside it.

## Method

Debug viewers from `605e3785` (old, copied to `D:/b575/old`) and `5359aba8` (new, `D:/b575/new`), both built with
`MSBUILDDISABLENODEREUSE=1 dotnet build managed/Tf2DemoSalvage.Viewer3D`. Every run through `run-exclusive.ps1`. Demo
`tools/corpus/local/demostf-cp_process_f12-2026-08-08-2207.dem` (main checkout's lcor).

**Frame cost**, interleaved O1, N1, O2, N2, each:

```
TF2VIEW_AUTOPLAY=1 pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 D:/b575/<old|new>/tf2demoview.exe \
  <demo> --tick 30000 --first-person --measure 20 +fps_max 0
```

CPU load (`Get-CimInstance Win32_Processor`, LoadPercentage) sampled before and after each run.

**Exposure over time and pictures**, interleaved EO1, EN1, EO2, EN2, each:

```
TF2VIEW_AUTOPLAY=1 TF2VIEW_FIXED_FRAME=0.0125 TF2VIEW_PLAY_SHOTS="<dir> 30050 30100 ... 30750" \
  pwsh .../run-exclusive.ps1 D:/b575/<old|new>/tf2demoview.exe <demo> --tick 30000 --first-person --measure 12
```

Fifteen shots, every 50 ticks. The scale is the one the capture log prints for each shot ("drawn with tone-map scale"),
the value the frame was drawn with (B243), from the viewer's log. Pictures are compared as `GoldenImageUiTests` compares
them: mean luma of a 4x3 grid of regions, tolerance 1.0, and each channel's region mean, tolerance 2.0.

## Control

- **Old against old (EO1 vs EO2)** sets the tolerance for the scale: the old read-back is not waited for, so its timing, and
  the scale, can differ between two runs of the same build. The tolerance is the largest |EO1 - EO2| over the fifteen shots.
- **The instrument reads a value that must be there**: each run's log must hold fifteen "tone-map scale" lines, and the
  scale must not be 1 at every shot (1 is the reset value, and an instrument reading only that would be reading nothing).
- **Same content**: old and new pictures at the first shot are compared before any exposure has had time to diverge.
- Frame cost pairs: O1 and O2 agree within 10%, or the pair is void.

## Environment

## Result

## Verdict
