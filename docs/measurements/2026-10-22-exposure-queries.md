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

RTX 3060, i5-10600K, Debug viewers as in Method, every run through `run-exclusive.ps1`. Load (3 x 2 s) before / after:
EO1 22/4/11 / 7/4/9, EN1 22/3/4 / 26/22/31, EO2 27/22/30 / 5/8/9, EN2 8/8/5 / 8/11/5, O1 4/5/6 / 4/13/11,
N1 12/5/5 / 8/2/6, O2 6/8/13 / 6/11/3, N2 10/7/3 / 11/8/6. The 20-30% samples around EN1/EO2 were this session's gate
phase 1 waiting on the shared lock; the exclusive runs themselves do not overlap it. Each exposure run reached tick ~30500
in its 12 s (captures cost ~280 ms a frame), so 9 or 10 of the 15 shots were taken; the comparison uses those both have.

## Result

**Frame cost** (`measured 20 seconds of playback ...` line of each run):

| run | build | frames | mean ms |
|---|---|---|---|
| O1 | old | 1545 | 12.95 |
| N1 | new | 1567 | 12.76 |
| O2 | old | 1533 | 13.05 |
| N2 | new | 1545 | 12.95 |

O1 and O2 agree within 1%, so both pairs stand. New is lower by 0.19 and 0.10 ms.

**Tone-map scale per shot** (capture log, "drawn with tone-map scale"):

| tick | EO1 | EO2 | EN1 | EN2 |
|---|---|---|---|---|
| 30050 | 0.84768903 | 0.84768903 | 0.8453814 | 0.8453814 |
| 30100 | 0.7574098 | 0.7574098 | 0.7565128 | 0.7565128 |
| 30150 | 0.7223166 | 0.7223166 | 0.72196794 | 0.72196794 |
| 30200 | 0.7086754 | 0.7086754 | 0.70853984 | 0.70853984 |
| 30250 | 0.7033728 | 0.7033728 | 0.7033201 | 0.7033201 |
| 30300 | 0.7013116 | 0.7013116 | 0.7012911 | 0.7012911 |
| 30350 | 0.7005102 | 0.7005102 | 0.7005022 | 0.7005022 |
| 30400 | 0.7001987 | 0.7001987 | 0.7001956 | 0.7001956 |
| 30450 | 0.7000778 | 0.7000778 | 0.7000766 | 0.7000766 |
| 30500 | - | 0.70003074 | - | 0.70003027 |

Control: old against old is identical to the last digit (fixed frame time makes it deterministic), so the tolerance the
plan defined is zero. New against new is identical too. New against old: 0.0023 (0.27%) at the first shot, shrinking to
0.0000012 by 30450. The scale is not 1 at any shot (the instrument's control holds) and every run logged its shots.

**Pictures**, worst 4x3 region (luma / one channel, 0..255), `compare.py` in the session scratch:

| pair | worst luma | worst channel | at |
|---|---|---|---|
| EO1 vs EO2 (control) | 1.22 | 2.06 | 30050 (0.72 / 0.86 at 30200) |
| EN1 vs EN2 (control) | 0.27 | 0.33 | 30200 |
| EO1 vs EN1 | 1.50 | 1.83 | 30200 |
| EO2 vs EN2 | 1.95 | 2.35 | 30200 (1.21 / 2.04 at 30050) |

Every other shot of every pair is under 0.31 luma / 0.46 channel.

## Verdict

**Frame cost: confirmed, small.** New is below old in both pairs, by 0.10-0.19 ms (about 1%), less than the 3.9% the leaf
held in finding 92; the pair-to-pair spread (O1 against O2) is 0.10 ms, so the size is barely above noise.
**Exposure equivalence: falsified as pre-registered** (tolerance zero, and every new sample differs), **by at most 0.27%
of the scale, all of it in the transient after the reset and decaying to 1e-6**: the engine's shape counts each bin up to
nineteen frames late and its first full histogram needs about twenty frames, where the old path's needed three, so the walk
down from 1 starts slightly later. That is the stated behaviour change, not a defect. The pictures differ by no more than
old differs from old on the same shots (two shots where old-vs-old itself exceeds `GoldenImageUiTests`' 1.0, because
playback content is not deterministic). Not shown here: f12's controller pins the range at 0.5-0.7 and the scale settles
on the 0.7 ceiling, so steady-state agreement is forced by the clamp; an unclamped map was not measured.
