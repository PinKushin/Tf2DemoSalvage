# The landing's particle rebuild, by method (B708, D221)

## Observation

`2026-11-08-landing-frame.md`: on `z1800.dem --tick 20000`, the landing frame is 1.16 to 1.39 s, of which `particles` is
0.60 to 0.83 s (`SLOW CAMERA`). That is the seek's rebuild, our addition (D221). `ParticleEffects.Bursts` replays each
rebuilt burst serially on the frame thread, because `Advance` asks the caller (`History`, `DormantAt`) tick by tick as it
goes, while a played frame's steps are asked first (`Plan`) and simulated on the workers.

## Question

Inside the landing frame's particle time, what share is the caller's lookups (`History`, `DormantAt`, `Model`), what share
is the effects' own simulation (`ParticleEffect.Step` and below), and what share is allocation and GC? That decides
whether the rebuild can be planned serially and simulated on the workers as a played frame is.

## Method

A CPU-sampling `dotnet-trace` of the same run, read with the `cpu-trace` probe over the landing frame's window, on the
frame thread, `under:ParticleEffects.Bursts`.

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build managed/Tf2DemoSalvage.Viewer3D -c Release
pwsh run-exclusive.ps1 dotnet-trace collect --profile cpu-sampling -o <scratch>/landing.nettrace -- \
  managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe \
  tools/corpus/demos/z1800.dem --tick 20000 --autoplay --measure 1
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- cpu-trace <scratch>/landing.nettrace <from> <to>
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- cpu-trace <scratch>/landing.nettrace <from> <to> <thread> 40 under:ParticleEffects.Bursts
```

The window is the landing frame: from the trace second of `skip landed at 20000` to its `SLOW FRAME` line, mapped by the
log's wall clock against the trace's start. The thread is the one whose top entry is `ViewerSession.RenderFrame`.

## Control

The probe's own control: `ViewerSession.RenderFrame` (or the frame's entry) should be near 100% inclusive of the chosen
thread's samples in the window, and `ParticleEffects.Bursts` should be a sizeable share of it, as the log says particles
were most of that frame. If `Bursts` is near zero, the window or the thread is wrong, not the rebuild cheap.

## Environment

## Result

## Verdict
