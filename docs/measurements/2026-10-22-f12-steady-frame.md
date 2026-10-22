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

## Result

## Verdict
