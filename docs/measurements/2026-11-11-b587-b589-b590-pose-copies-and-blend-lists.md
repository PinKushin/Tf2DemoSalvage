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

The owner's desktop, Debug builds, 984×551, window off screen, run 2026-11-11 to 11-12. **The machine was shared**: a live
`tf_win64` and other agents' builds and browsers ran throughout, and `Win32_Processor.LoadPercentage` read 46-100% around
the runs (each run's before/after load is in its row). Before: `origin/main` @ `75767af8`, its build copied aside so the two
builds could be interleaved. After: the branch with the B587 and B589 changes, uncommitted at run time (the `perf:` commit
after this record).

## Result

Raw summary lines, in run order (`measured`, `phases`, `gc:`; the full reports went to the console only):

```
== before 1 (load 81 before; the first run's summary was lost to a tail cut, this is the re-run)
measured 20 seconds of playback, 19 samples (9 rebuild reports), ending at tick 7287; 848 frames, mean 23.59 ms
  phases, mean ms: sound 0.61, camera 19.81, project 0.00, advance 1.52, capture 0.00, hud 0.70, draw 0.05, between 0.89; render thread draw 3.18
  gc: paused 0.79 ms a frame; collections gen0 115, gen1 29, gen2 0; allocated 1.03 MB a frame; render thread 399,973 bytes a frame
== after 1 (load 51 -> 69)
measured 20 seconds of playback, 19 samples (17 rebuild reports), ending at tick 7305; 2368 frames, mean 8.45 ms
  phases, mean ms: sound 0.17, camera 6.75, project 0.00, advance 0.75, capture 0.00, hud 0.45, draw 0.01, between 0.30; render thread draw 2.29
  gc: paused 0.20 ms a frame; collections gen0 182, gen1 26, gen2 0; allocated 0.53 MB a frame; render thread 317,450 bytes a frame
== before 2 (load 82 -> 70)
measured 20.1 seconds of playback, 19 samples (11 rebuild reports), ending at tick 7290; 1266 frames, mean 15.84 ms
  phases, mean ms: sound 0.35, camera 13.32, project 0.00, advance 1.00, capture 0.00, hud 0.51, draw 0.04, between 0.61; render thread draw 2.64
  gc: paused 0.35 ms a frame; collections gen0 115, gen1 23, gen2 0; allocated 0.67 MB a frame; render thread 379,077 bytes a frame
== after 2 (load 46 -> 51)
measured 20 seconds of playback, 19 samples (17 rebuild reports), ending at tick 7309; 2504 frames, mean 7.99 ms
  phases, mean ms: sound 0.16, camera 6.57, project 0.00, advance 0.65, capture 0.00, hud 0.34, draw 0.02, between 0.23; render thread draw 2.04
  gc: paused 0.16 ms a frame; collections gen0 188, gen1 26, gen2 0; allocated 0.52 MB a frame; render thread 319,763 bytes a frame
== before 3 (load 88 -> 54)
measured 20 seconds of playback, 19 samples (16 rebuild reports), ending at tick 7309; 2537 frames, mean 7.88 ms
  phases, mean ms: sound 0.16, camera 6.51, project 0.00, advance 0.64, capture 0.00, hud 0.35, draw 0.02, between 0.21; render thread draw 1.89
  gc: paused 0.20 ms a frame; collections gen0 199, gen1 26, gen2 0; allocated 0.54 MB a frame; render thread 331,265 bytes a frame
== before 4 (load 59 -> 88)
measured 20 seconds of playback, 19 samples (14 rebuild reports), ending at tick 7287; 2203 frames, mean 9.08 ms
  phases, mean ms: sound 0.18, camera 7.61, project 0.00, advance 0.66, capture 0.00, hud 0.35, draw 0.02, between 0.25; render thread draw 1.99
  gc: paused 0.20 ms a frame; collections gen0 177, gen1 27, gen2 0; allocated 0.56 MB a frame; render thread 336,372 bytes a frame
== after 3 (load 100 -> 85)
measured 20 seconds of playback, 19 samples (17 rebuild reports), ending at tick 7311; 2526 frames, mean 7.92 ms
  phases, mean ms: sound 0.17, camera 6.30, project 0.00, advance 0.76, capture 0.00, hud 0.38, draw 0.01, between 0.29; render thread draw 2.22
  gc: paused 0.21 ms a frame; collections gen0 199, gen1 31, gen2 0; allocated 0.55 MB a frame; render thread 312,902 bytes a frame
```

Control: every run ended at tick 7287-7311 from 5970, 1317-1341 ticks in 20 s against 1334 expected, so each measured
playback. The per-second pose lines of after 1 read `posed 3.7-14 of 1854-1855 selected`, `collated unparented 234-235`.

**Only the runs above 2,200 frames are comparable**: before 1 and 2 ran at 848 and 1,266 frames under 80% load, and their
allocation a frame is inflated by work paid per tick rather than per frame. Comparable: before 0.54 and 0.56 MB, 7.88 and
9.08 ms; after 0.53, 0.52 and 0.55 MB, 8.45, 7.99 and 7.92 ms.

## Verdict

**Falsified as stated, and inconclusive in substance.** After 3 (0.55 MB) did not fall below before 3 (0.54 MB); the means
are 0.533 after against 0.550 before, a 0.017 MB difference inside the 0.01 MB rounding and the 0.03 MB run spread. The
frame mean did not move outside the load noise (7.88-9.08 before, 7.92-8.45 after). The allocations removed are proved
per call by the unit tests (`IkFor_ARepeatCall_AllocatesNothing`, `BlendedCyclesPerSecond_ARepeatCall_AllocatesNothing`,
`Instances_TwoFramesOfOneEntity_RefillTheSameTransitionList`), but this instrument on this shared machine cannot resolve
their frame share. A quiet-machine re-run, or an allocation trace filtered to these methods, is what would.

B590 was not changed. Its repeat is the instance build's `SelectWith` for each collated prop, about 235 a frame (the
pose line's `collated unparented`), plus the pre-posed roots (under 10): one dictionary probe and a handful of multiplies
each. At an interpolated 20-50 ns a call that is 5-12 µs, at or under the 0.01 ms the owner calls small; not measured.

## Re-run on a quiet machine (2026-11-12, by the lead)

Same command, same builds (the before build copied aside, the after build at `a600c39c`), interleaved before/after three
times, with no TF2 and no other agent running; `Win32_Processor.LoadPercentage` read 1-11% before each run.

| pair | before, allocated a frame | after, allocated a frame |
|---|---|---|
| 1 | 0.49 MB | 0.49 MB |
| 2 | 0.51 MB | 0.49 MB |
| 3 | 0.51 MB | 0.49 MB |

**Allocation falls or holds in every pair** (mean 0.503 to 0.490 MB, about 0.013 MB a frame, inside the prediction's
0.005-0.06 MB). Still at the line's 0.01 MB resolution, so the size is approximate; its sign is now consistent. The
frame means were not captured: the re-run's filter kept only the first twelve report lines, which are the per-second
lines, not the run's summary. So the frame share stays unresolved, as above.
