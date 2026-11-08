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

The owner's desktop machine, Windows 11, viewport 1280x720, Release build of `b9965f41`, under the desktop lock, no other
viewer or agent running. `dotnet-trace` 10.0.745401 with `--profile dotnet-common,dotnet-sampled-thread-time` (the
10.x name for the old `cpu-sampling`); its EventPipe sampler lands on safepoints, so `BulkMoveWithWriteBarrier` shares are
its bias, as `2026-11-06-detail-sprites-visible-leaves.md` found. Load before and after was not recorded. The trace's
viewer log is `viewer-20261108-163811-36772.log`: the landing frame there was `SLOW FRAME 1283 ms … camera 862.5`.

## Result

The trace lasts 64.1 s. **The window 60.5 to 62.6 s is approximate**: it maps the log's wall clock against the trace's
start by the process launch, so it holds the landing frame and the last walk frames before it, as the walk's decal
replay in it shows. Frame thread 27480, 962 samples in 2.1 s; the control holds (`ViewerSession.RenderFrame` 99.6%):

```
  99.6%  Tf2DemoSalvage.Presentation.ViewerSession.RenderFrame()
  47.3%  Tf2DemoSalvage.Presentation.FramePresenter.StepSkip(...)
  41.2%  Tf2DemoSalvage.Presentation.DecalEffects.StepReplayOver(...)
  38.4%  Tf2DemoSalvage.Presentation.FramePresenter.DrawOver(...)
  29.8%  Tf2DemoSalvage.Presentation.BurstEffects.Step(...)
  29.7%  System.Buffer.BulkMoveWithWriteBarrier(...)
  27.4%  Tf2DemoSalvage.Scene.ParticleEffects.Bursts(...)
  25.8%  Tf2DemoSalvage.Scene.ParticleEffects.Advance(class BurstSlot, ...)
  24.1%  Tf2DemoSalvage.Content.Assets.ParticleEffect.SubStep(float32,bool)
  22.7%  Tf2DemoSalvage.Presentation.MomentPresenter.Show(...)
```

So `Bursts` is about 575 ms of the window. Of it, `Advance` is 25.8 points and the effects' own `SubStep` 24.1, so the
caller's lookups and the rest of `Advance` are about 1.7 points (about 35 ms), and the simulation about 93% of the rebuild.
Thread 2524 is the corpse recorder (`CorpseRecord.Run`, 100%), on a pool thread, not the frame's.

## Verdict

The rebuild's time is the effects' simulation, not the caller's lookups. A rebuild's step ends, dormancy and points do not
depend on the effect's state, so they can be asked serially and the simulation run on the particle workers as a played
frame's is. The cost to beat is about 0.6 s of the landing frame. *Not settled:* the window's edges, so the 575 ms is the
window's, not the landing frame's alone.
