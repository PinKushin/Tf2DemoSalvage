# Measurement 2026-10-30: where the face-flex evaluation's time goes (`flex`, inside `place`)

## Record (written and committed with the instrument, before the first run)

## Observation

The lead's B597 runs (`docs/measurements/2026-10-30-b597-gpu-split/01-N.txt`, `06-N.txt`, `07-N.txt` on
`perf/b597-gpu-split`) print, on f12 from tick 30000, gummo first person, 1280x720, `+mat_queue_mode 2`, a `moment cost`
line whose `place` column is 0.8-1.1 ms a rebuild and whose `flex` part (`PlaceFlexTicks`, `EntityModelSet.FlexFor`
whole) is 0.60-0.72 ms of it: the largest single part of the pose's `place` step. The lead's estimate (an estimate, not a
count) is 5-6 flexed faces a frame there, about 0.1 ms a face, which looks like more than the arithmetic needs.

## Question

On that scene, which part of `FlexFor` takes the 0.6-0.7 ms a rebuild: the global weights (`FaceDriving`: the scene and
voice lookups and `ActorFace.Step`), the controller lookup (`FaceFlex.Local`), the rules (`RunFlexRules`), the delay,
the last-face probe, the flexed-slot lookups, the scratch clear, the accumulation, the write into the flexed slots, or
the resting faces? And how many faces, of which kind (own, worn item, corpse, resting), hit or miss the last-face cache,
and how many vertex animations do the misses walk?

## Hypothesis

The cost is per driven face and mostly not vertex work: `ActorFace.Step` (a dictionary of every controller rebuilt by
name each call, scene events walked) and the per-call scene lookup are the largest, and nearly every driven face misses
the last-face cache, because the controllers decay by 0.95 each call and so the weights change every frame.

## Prediction (before the runs)

Over each run's `moment cost` lines (the means of 100 rebuilds each, then the mean of those lines):

1. `flex` 0.55-0.80 ms a rebuild (the lead's figure reproduces).
2. `driving` is the largest of the ten timed parts (`driving`, `local`, `rules`, `delayed`, `probe`, `corners`, `clear`,
   `deltas`, `slots`, `resting`) and at least 0.25 ms a rebuild.
3. misses are at least 0.8 of hits plus misses.
4. faces 3-12 a rebuild.

## Falsified if

Any one of, in the mean of the three runs:

1. `driving` is not the largest of the ten parts, or is under 0.25 ms.
2. misses are under 0.8 of hits plus misses.
3. `flex` is outside 0.55-0.80 ms (the scene is not the lead's; the split is still read, but the comparison with the
   lead's figure is void).
4. faces outside 3-12.

## Instrument

`FlexCounters` (`managed/Tf2DemoSalvage.Scene/FlexCounters.cs`): stopwatch ticks and counts added inside
`EntityModelSet.FlexFor`, `FaceDriving` and `RestingFaceOf` where each piece of work runs (B243), carried on
`EntityModelSet.PoseCounters.Flex`, differenced per rebuild like every other pose counter, and printed by
`MomentCostLog` as `flex parts (...)` right after `place (... flex N)`, in milliseconds at three decimals and counts per
rebuild. `driving` contains `scenes`, `voices` and `step`; the other nine parts are disjoint from it and from each other.
`delayed` contains the copy of the delayed weights a miss stores; `clear` contains the scratch growth and a new stream;
`corners` both flexed-slot lookups. The production code's only change is that `FaceFlex.Deltas` is taken apart into its
two halves (clear, then `StudioFlexRules.Accumulate`) inside `FlexFor`, the same calls in the same order, and the scene
and voice arguments of `ActorFace.Step` evaluated into locals in the order C# evaluated them.

Tests: `MomentCostLogTests.Report_WithFlexParts_NamesEachAsAMeanPerRebuild` (each part a distinct width, a mean not a
sum); `FaceFlexAnimationDemoTests.FlexCounters_AMedicPlayingAFlexAnimation_CountEachDrawOnce` (production path: 40 draws
are 40 own faces, each a hit or a miss, vertices walked).

## Method

Build (Release) from a `git archive` of the commit that adds this file, extracted no-clobber into a new folder
`D:/measure-f12/flex-a-src`, `MSBUILDDISABLENODEREUSE=1 dotnet build managed/Tf2DemoSalvage.Viewer3D -c Release`, the
exe copied no-clobber to `D:/measure-f12/flex-a/`. Three runs, `01-A`, `02-A`, `03-A`, by
`docs/measurements/2026-10-30-flex-for-cost/all.ps1`, each through `run.ps1` there: the quiet gate (no viewer, dotnet,
testhost or MSBuild process, then three CPU load samples under 25%), then under `run-exclusive.ps1`:

`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 tf2demoview.exe D:/measure-f12/f12.dem --tick 30000 --spectate gummo
--first-person --measure 20 +fps_max 0 +mat_queue_mode 2`

Each run's stdout is `<name>.txt` in that folder. Read: every `moment cost` line's `flex`, `place`, and `flex parts`, the
`frame rate` line's mean and `camera` part; each figure is the mean of a run's lines, then reported per run and as the
range over runs.

## Control

- The parts add up: `driving + local + rules + delayed + probe + corners + clear + deltas + slots + resting` is within
  15% of `flex` (what is outside is the guard, the timestamps themselves, and `FlexedInstances`), and `scenes + voices +
  step` is at most `driving`.
- The counts are consistent: `hits + misses == own + worn + corpse`, `own + worn + corpse + resting == faces`,
  `faces <= calls`.
- Something that must be present: `faces > 0`, and when `misses > 0`, `vertices > 0`.
- Playback: ticks advanced about 66.7 a second of measured playback (the skill's control).

If any fails, the instrument is wrong and nothing is read from the split.

## Environment

The owner's desktop machine (the B597 runs' machine), 1280x720 viewport, f12 at `D:/measure-f12/f12.dem`. Build: the
commit adding this file on `perf/flex-for-cost` (parent `19f59cc3`). Load before and after each run: in `runlog.txt`.

## Result

## Verdict
