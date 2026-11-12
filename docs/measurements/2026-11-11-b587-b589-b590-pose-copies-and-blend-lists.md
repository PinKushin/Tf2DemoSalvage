# 2026-11-11 — bone copies, blend-input lists and the doubled frame selection (B587, B589, B590)

## Observation

Three filings from the 2026-10-24 Fable read of `main` @ `01faad62`, each **read from code, not measured** (docs/RISKS.md):

- B587: `SkeletonPose` reads its bones through `IReadOnlyList<StudioBone>` in the per-bone loops (`QuatInterp`, `Jiggle`,
  the IK rebuild, `FlagsOf` from `BoneMergeCache`), each read an interface call that copies the 136-byte struct; and
  `BoneMergeCache.MergeMatchingBones` runs `SpreadOf` (a second walk of the merged pairs) before its log-level test.
  Estimate 0.15-0.3 ms.
- B589: the blend inputs allocate per player per frame: `TransitionsFor` returns a new `List` every call, and
  `BlendedAnimations` (from `BlendedCyclesPerSecond` in simulate and `IkFor` in the build) a `List` plus `ThreeWay`'s two
  arrays. Estimate 0.05-0.15 ms.
- B590: the baked-frame selection `SelectWith` is computed in `SettleEach` for every prop, again in `PrePoseRoots`, and
  again in `Instances`. Estimate ~0.1 ms.

## Question

On f12's fight, first person, what do the frame mean, the pose line's `setup` and `simulate`, and the allocation a frame
read before and after the B587 and B589 changes?

## Prediction

- Allocation a frame falls by 0.005-0.06 MB (B589: about two dozen players, a few hundred bytes each in simulate and per
  IK'd layer in the build).
- `setup` falls or holds; a fall under 0.01 ms is below the line's resolution.
- The frame mean moves by less than run-to-run noise (about 0.2 ms).

Falsified if the allocation a frame does not fall in both after-runs against both before-runs.

## Method

Two runs on each build, same command, same demo and tick, the window off screen:

```
TF2VIEW_WINDOW_POS=-3000,100 TF2VIEW_AUTOPLAY=1 pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 \
  managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe D:/measure-f12/f12.dem \
  --tick 5970 --first-person --measure 20 +fps_max 0
```

Before: `origin/main` @ `75767af8`. After: branch `perf/b587-b589-b590` with the changes. Each build by
`MSBUILDDISABLENODEREUSE=1 dotnet build managed/Tf2DemoSalvage.Viewer3D`, the summary line read.

Read from each report: the frame mean, `setup`, `simulate`, `place` on the pose line, the `gc:` allocation a frame,
`posed N of M` and `built B`.

## Control

- Ticks advanced against 66.7 × 20 s, so a run that measured load rather than playback is caught.
- `posed N of M` and `built B` must agree between before and after: the same workload was posed. A change in them means the
  runs are not comparable, not that the change helped.
- B590 is not changed between the builds. Its repeat is bounded instead: the selections repeated a frame are the
  collated props (`posed N of M`'s M) plus the pre-posed roots, each one dictionary probe and a few multiplies.

## Environment

## Result

## Verdict
