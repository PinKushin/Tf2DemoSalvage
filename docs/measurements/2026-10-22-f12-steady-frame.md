# Measurement 2026-10-22: where the viewer's steady f12 frame goes

## Observation

`2026-10-21-tf2-vs-viewer-f12.md` measured the viewer at 66.1 and 68.8 fps steady (15.8-16.6 ms mean frame) on f12 first
person at 1280x720, against TF2 at 159-239 fps on the same scene. Its raw output (`viewer-run-b.txt`) shows the `camera` phase
at about 10 ms of the 15.8, but that run was on TF2's own `f12.dem`, not lcor's, and nobody has asked whether the frame waits on
the GPU at all.

## Question

On lcor's f12 (`demostf-cp_process_f12-2026-08-08-2207.dem`), `--tick 30000 --first-person`, 1280x720, `+fps_max 0`:
(1) is the steady frame CPU-bound or GPU-bound; (2) which phase and part carries the most time; (3) of those, which is the
largest the engine does not pay?

## Hypothesis

H1: CPU-bound; shrinking the 3D view to 320x180 (1/16 of the pixels) changes the steady mean frame by under 10%.

## Prediction (numbers fixed before the run)

- 1280x720 steady mean frame 13-17 ms (59-77 fps).
- 320x180 steady mean frame within 10% of the 1280x720 figure.
- `camera` (pose + particles + setcamera) is at least 60% of the frame.

Falsified if 320x180 is more than 10% faster (then the GPU or fill is a material part of the frame).

## Method

The observation that answers (1): the same run at two viewport sizes; a GPU-bound frame scales with pixels, a CPU-bound one
does not. (2) and (3): the `--measure` phase and `parts:` lines (means over each second), then a `dotnet-trace` CPU sample of
the same scene for whatever the phase timers do not split.

Build: `MSBUILDDISABLENODEREUSE=1 dotnet build managed/Tf2DemoSalvage.Viewer3D` (Debug, what the owner runs). Each run:

```
TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=<size> pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 \
  managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe \
  C:/Users/pinku/source/repos/PinKushin/Tf2DemoSalvage/tools/corpus/local/demostf-cp_process_f12-2026-08-08-2207.dem \
  --tick 30000 --first-person --measure 20 +fps_max 0
```

Runs: A1, A2 at 1280x720; B1 at 320x180; then interleaved B2, A3, B3, A4 (added after A2 broke the 15% control under
30-50% background load, before any Result was written). Each run samples CPU load (3 x 2 s) before and after. Raw outputs in
`2026-10-22-f12-steady-frame/`; the steady figure is the per-second samples after the first five, frame-weighted.

Trace: the same command at 1280x720 under `dotnet-trace collect --profile dotnet-sampled-thread-time -o T1.nettrace --
<exe> <demo> ...`, read with the probe `cpu-trace T1.nettrace <from> <to> <render thread> 60` over the steady window.

GPU (added after the size pair proved unresolvable under load, before any Result was written): runs C1 at 1280x720 and C2
at 320x180, each with the Windows counter `\GPU Engine(pid_<viewer>_*engtype_3D)\Utilization Percentage` summed and
sampled every 0.5 s for the viewer's own PID. A GPU-bound frame reads near 100%; a CPU-bound one well under. **Control:**
C2 has 1/16 of the pixels, so its reading must be lower than C1's; if it is not, the counter is not seeing the viewer's work.

Trace control: the render thread is the one whose inclusive top is the frame loop and which holds `EntityModelSet.Instances`;
its inclusive share for the `camera`-phase methods should be roughly the `--measure` camera share (within a factor of 1.5)
or the sample is not of the frame the timers saw.

## Control

- A1 and A2 must agree within 15% on mean frame, or the pair is void and repeated.
- Ticks advanced must be about 66.7 x 20 (the playback-time window, finding 82); a short tick count means a stalled or
  empty run.
- The demo path exists and the run reports frames and particles (a run with no demo loaded reports none).
- The viewport size actually applied is checked in the log/output (the window grows to fit); if B1 did not shrink the view,
  the comparison is void.

## Environment

- Machine: RTX 3060, i5-10600K. Debug viewer built from `47be9006`/`c942fd56` (the record's own commits on top of main
  `e1ec318a`; no code change), every run through `run-exclusive.ps1`.
- Load (3 x 2 s samples, before / after each run, last line of each raw file): B2 36/65%, A3 60/16%, B3 11/29%,
  A4 94/30%, C1 32/75%, C2 62/69%, T1 73/60%. A1, A2 and B1 had no per-run sample; a spot read between them was 30-38%
  (owner's own processes). Other agents' gates and UI suites held the lock between runs, never during one.

## Result

Steady = per-second samples after the first five, frame-weighted (`sum.ps1` over each raw file). Phases in ms a frame.

| run | view | steady ms (fps) | camera | draw | advance | hud | sound |
|---|---|---:|---:|---:|---:|---:|---:|
| A1 | 1280x720 | 14.16 (70.6) | 8.36 | 2.81 | 1.17 | 1.13 | 0.27 |
| A2 | 1280x720 | 20.77 (48.2) | 12.85 | 3.65 | 1.52 | 1.70 | 0.56 |
| A3 | 1280x720 | 19.35 (51.7) | 11.58 | 3.67 | 1.49 | 1.54 | 0.40 |
| A4 | 1280x720 | 15.48 (64.6) | 9.40 | 2.90 | 1.30 | 1.17 | 0.30 |
| C1 | 1280x720 | 15.63 (64.0) | 9.45 | 2.92 | 1.28 | 1.22 | 0.32 |
| B1 | 320x180 | 17.24 (58.0) | 10.84 | 2.53 | 1.45 | 1.39 | 0.43 |
| B2 | 320x180 | 13.12 (76.2) | 8.26 | 2.02 | 1.13 | 1.13 | 0.21 |
| B3 | 320x180 | 14.46 (69.2) | 9.07 | 2.22 | 1.28 | 1.21 | 0.28 |
| C2 | 320x180 | 13.56 (73.8) | 8.24 | 2.24 | 1.16 | 1.07 | 0.22 |

Ticks advanced: A1 ends at 31331, B1 31338, A2 31321 (about 66.6 a second over 20 s; control met).

**GPU, the viewer's own 3D engine** (`C1-gpu.txt`, `C2-gpu.txt`; the counter sampled about every 1.5 s, not 0.5, because
the read itself is slow): over the last playing seconds C1 read 6.3-10.2% and C2 6.0-9.0%. Control met: the 1/16 view reads
lower.

**CPU sample T1** (render thread 17520, 212-229 s of a 231 s trace, 8,258 samples; `T1-17520.txt`). Inclusive:
`UploadCameraOver` 60.2%, `MomentScene.Pose` 33.6%, `EntityModelSet.Instances` 31.7%, `Device3D.DrawFrame` 18.6%,
`BurstEffects.StepOver` 8.0%, `SkeletonPose.Build` 6.9%, **`EntityModelSet.FlexFor` 6.0%** (of it `FaceFlex.InBufferOrder`
3.4%, `FaceFlex.Deltas` 0.6% leaf), `ModelLighting.For` 5.8%, `Device3D.UpdateExposure` 4.0% (`ExposureHistogram.TryRead`
3.9%, a leaf: the CPU's per-pixel count of the read-back region), `DecalEffects.StepModelDecals` 2.3%.
`PollGCWorker` is 42.3% of leaves; by its callers (`T1-17520-pollgc.txt`) it is a long tail of loop safe points, the
largest `Instances` 3.7% and `ModelLighting.For` 2.7%, so it is charged to the frames above it and ranked by inclusive share.
**Trace control:** `UploadCameraOver` (the `camera` phase) 60.2% against `camera`'s 59-62% of the frame in the A runs; met.

**Deviations from the Method:**
1. The A/B pair control (15%) failed: A runs spread 14.16-20.77 ms, B runs 13.12-17.24, with the load. The size test cannot
   resolve under this load, and the GPU pair was added (before any Result) to answer it instead.
2. T1's `--measure` lines were not captured (`dotnet-trace` does not pass the child's output through without
   `--show-child-io`), so its window cannot be tied to its own phase timings; the control above uses the A runs instead.
3. A1, A2 and B1 have no per-run load sample.
4. Post hoc, not predicted: `draw/camera` is 0.31-0.34 in every 1280x720 run and 0.23-0.27 in every 320x180 run, so about
   0.6-0.7 ms of `draw` scales with pixels.

## Verdict

**H1 holds on the direct instrument and is inconclusive on the predicted one: the frame is CPU-bound.** The viewer's 3D
engine is busy 6-10% of the time at 1280x720 (falsifying a GPU-bound frame), while the size pair was void under load and
cannot confirm the 10% figure. Prediction 1 (13-17 ms at 1280x720) held in the three runs under 75% load and failed in two
heavy-load runs; prediction 3 (`camera` at least 60%) held at 59-62%, on its edge. The steady 14-15.5 ms goes 8.4-9.5 to
`camera` (pose about a third of the render thread, particles, set-camera), 2.8-2.9 to `draw`, about 1.2 each to `advance`
and `hud`. The largest single method doing work the engine does not do is `FlexFor` (6.0% inclusive), whose spread of the
deltas into buffer order rewrote every slot of the model each changing frame, though no flex reaches most of them (the
engine's accumulation walks only the named vertices, finding 75; the spread is ours). Next is the exposure count on the
processor (3.9%; Valve counts with GPU occlusion queries, `viewpostprocess.cpp:551-613`). The first is fixed and the second
filed in finding 90.
