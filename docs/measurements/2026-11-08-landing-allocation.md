# What the landing frame allocates (B708)

## Observation

`2026-11-08-landing-replay-on-workers.md`: the landing frame on `z1800.dem --tick 20000` runs 52 to 68 gen0 collections and
pauses 136 to 158 ms for GC (the `SLOW FRAME` line's `gc` part), and with the particle replay on the workers its particle
time stayed 365 to 417 ms where the threads predicted 100 to 300. A collection stops every worker, so allocation would
serialise the parallel replay. That link is a lead, not measured.

## Question

What does the landing frame allocate, by type and by the frame that allocated it, and how much of it is the particle
replay (`ParticleEffect.SubStep` and below)?

## Method

An allocation-sampling `dotnet-trace` of the same run, read with the `alloc-trace` probe over the landing window, every
thread, all sizes.

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build managed/Tf2DemoSalvage.Viewer3D -c Release
pwsh run-exclusive.ps1 dotnet-trace collect --providers Microsoft-Windows-DotNETRuntime:0x1:5 -o <scratch>/landing-alloc.nettrace -- \
  <worktree>/managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe <worktree>/tools/corpus/demos/z1800.dem \
  --tick 20000 --autoplay --measure 1
pwsh run-exclusive.ps1 dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- alloc-trace <scratch>/landing-alloc.nettrace 4 all <from> <to> any
```

The window is the landing frame, mapped from the run's log (`skip landed at 20000` to the landing `SLOW FRAME`) against the
trace's start, with the earlier trace's offset as a guide; reported as approximate.

## Control

The probe's own: the sampled total over the window, at about 100 KB a `GCAllocationTick`, should be of the order the
frame's gen0 count implies. With 52 to 68 gen0 collections, that is hundreds of MB, not a few. A total far below that means
the window or the trace missed the frame.

## Environment

## Result

## Verdict
