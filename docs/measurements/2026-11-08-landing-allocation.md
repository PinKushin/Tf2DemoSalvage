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

The owner's desktop machine, Windows 11, viewport 1280x720, Release build of `ca2dcdd6`, under the desktop lock, no
other viewer or agent running. `dotnet-trace` 10.0.745401 with `Microsoft-Windows-DotNETRuntime:0x1:5`. The viewer log is
`viewer-20261108-174150-30032.log`: Main entered 782 ms after the process started at 17:41:50.386, the landing frame
`SLOW FRAME 1029 ms … gc 57/11/0` ended at 17:42:54.144. Load before and after was not recorded.

## Result

**The window, approximate:** 63.4 to 64.6 s from the trace's start, the landing frame mapped by the process start;
and 64.7 to 65.6 s, the played frames after it. The control holds: 434 MB in the landing window, of the order 57 gen0
collections imply.

Landing window, by thread (rates scaled to 20 s by the probe):

```
3975 large ticks over 1.2 s; 434 MB, 7241 MB per 20 s
by thread: 26236 3599 MB/20s, 7948 3376 MB/20s, 23100 76 MB/20s, 5380 76 MB/20s, 36680 61 MB/20s, 3176 49 MB/20s
    3599 MB/20s  thread 26236 under Tf2DemoSalvage.Presentation.ViewerSession.RenderFrame
    3155 MB/20s  thread 7948 under Tf2DemoSalvage.Scene.CorpsePhysics.Advance
      56 MB/20s  thread 23100 under Tf2DemoSalvage.Content.Assets.ParticleEffect.Simulate
      49 MB/20s  thread 5380 under Tf2DemoSalvage.Content.Assets.ParticleEffect.Simulate
      42 MB/20s  thread 36680 under Tf2DemoSalvage.Content.Assets.ParticleEffect.Simulate
      29 MB/20s  thread 3176 under Tf2DemoSalvage.Content.Assets.ParticleEffect.Simulate
```

The frame thread's largest in the landing window: `Projected[]` 442 MB/20s (`StudioDecalProjection.Project`), `SceneProp`
319 (`PlayerProps.Add` 263, `UpdateClientSideAnimations` 46), `WorldVertex[]` 192 (`DecalMesh.Fan`,
`DecalEffects.PlaceModelDecal`).

Played window after the landing:

```
2061 large ticks over 0.8 s; 210 MB, 4942 MB per 20 s
by thread: 7948 3666 MB/20s, 26236 1123 MB/20s, 36860 84 MB/20s, 22000 38 MB/20s, 36680 10 MB/20s, 3176 10 MB/20s
    3524 MB/20s  thread 7948 under Tf2DemoSalvage.Scene.CorpsePhysics.Advance
```

Thread 7948's types in the played window, MB per 20 s: `<Walk>d__20` 455 (from `IvpLedgeTopology.<EndingAt>` under
`IvpMindistMinimize.Solver`), `<EndingAt>d__17` 376 (`Solver.PointFaceProximity`, `PointEdgeProximity`),
`IvpContactRecord` 333, `Double[]` 330 (`IvpComplementaritySolver..ctor` from `IvpFrictionSystem.SolveNormals`),
`IvpLedgeSide` 201, `ValueTuple<long,long>[]` 194, `Single[]` 187, `IvpRigidBody[]` 141 (`IvpSimulationUnit.RebuildEntries`).

## Verdict

**The particle lead is falsified:** the particle workers allocate about 10 MB of the landing's 434 MB. The allocation is
the corpse physics, on thread 7948 under `CorpsePhysics.Advance`, at 3.4 to 3.7 GB per 20 s both in the landing and in
the played frames after it, and the frame thread's own, 1.1 GB per 20 s played and 3.6 at the landing (decal projection and
scene props). The corpse physics' garbage is steady, not a landing cost, so every frame pays its collections. The first
target is the IVP iterators, `<Walk>` and `<EndingAt>`, 0.83 GB per 20 s of it.
