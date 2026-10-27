# Measurement 2026-10-26: `r_threaded_particles` on current main, and the sun over the whole frame

**Status: plan, written before any run.** Nothing below the Method has been measured yet.

## Observation

1. Finding 94 measured the fork-join of the per-effect particle step at `bursts/step` 0.36 → 0.28 ms on f12 first person
   (`2026-10-22-threaded-particles.md`, on branch `worktree-agent-a15ceb35cdf667d9a`), behind an invented
   `cl_frame_workers`. Finding 103 showed the engine does exactly this (`ParallelProcess( …, ProcessPSystem )`,
   particlemgr.cpp:1869-1897, `r_threaded_particles` default 1 at :1543). Branch `perf/threaded-particles-sunfrom` ports the
   step split onto main behind `r_threaded_particles` (the build stays serial, as the engine's does), with a fork of
   degree physical cores − 1 (the game thread included) and a size threshold, `TF2VIEW_PARTICLE_THRESHOLD`, default 4.
2. The CPU profile put `LevelLighting.SunFrom` at 5.8% exclusive. B594's census counted ≤ 2 calls a frame, but only
   across the two world `Instances` calls; static props' first state, the viewmodel pass and an impact's colour also ask.
   The branch adds `LevelLighting.SunWork` (calls by caller, ticks inside `SunFrom`, timed only under `--measure`) and
   `SunMeter`, which differences it across whole frames and prints `sun per frame …` in the once-a-second census note and
   `sun` among the parts.

## Questions

- A. How much do `camera particles/bursts/step`, `camera particles`, `camera` and the frame change between
  `+r_threaded_particles 0` and `1` at the default threshold, in ms and in percent of the frame?
- B. Does threshold 0 or 16 do better than 4 (one T configuration each)?
- C. How many sun calls a frame, from which caller, and how many ms, on f12 gummo first person and in the empty view?

- D (added after the probe, before the grid): a 15 s probe run (`00-probe.txt`, build of `849d94c0`) read the sun at
  0.14-0.34 ms a frame for 0.56-2.04 calls, all light cache builds: about 0.2 ms a CALL. Reading `MapLevel.StrikesSky`:
  the brush trace stops at the skybox, but `DisplacementCollision.SweepSurface` swept every displacement whose box met
  the whole 57,016-unit travel box. `7057f223` bounds the terrain sweep by the brush fraction (bit-identical by
  `DisplacementSweepLimitTests`). How much does `sun` fall between A (`849d94c0`) and C (`7057f223`), threaded both?

## Prediction

- D: `sun` falls from ~0.15-0.3 to under 0.03 ms a frame on gummo with the same calls a frame; the frame by about that.
  Falsified if C's `sun` per call is above half of A's.

- A: `bursts/step` falls 0.05-0.10 ms (finding 94: 0.08); `camera particles` by the same; the frame by under 0.1 ms,
  inside run spread. Falsified if T's `bursts/step` is not below S's in every pair, or any T part rises above S by > 15%.
- B: 0 is no better than 4 (f12's played lists are tens of effects, above either); 16 equal to 4. Inconclusive within spread.
- C: gummo: light cache builds ≤ 2 a frame steady (B594), static props near 0 steady (memoised per origin) with bursts
  as the camera turns, direct ≈ impacts a frame; total ms ≤ 0.05. Empty view: near 0. If the total is above 0.1 ms, the
  profile's 5.8% is real and the engine is read for the reduction; if it is ≤ 0.01 ms, the profile share is attribution
  bias and nothing is changed.

## Method

One Release build of the branch: `run-exclusive.ps1 -Shared dotnet build managed/Tf2DemoSalvage.Viewer3D -c Release`,
copied to `D:/measure-f12/threaded-sun/`. One viewer at a time through `run-exclusive.ps1`, the quiet gate of
`2026-10-25-render-thread-cpu-vs-gpu/run.ps1` (three `Win32_Processor.LoadPercentage` samples under 25%, no other
`tf2demoview`/`dotnet`/`MSBuild`) before each run, 60 s of playback each:

- gummo: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 <exe> D:/measure-f12/f12.dem --tick 30000 --spectate gummo
  --first-person --measure 60 +fps_max 0 +r_threaded_particles <0|1>`
- empty: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="1613 -2821 154 89 0" <exe>
  D:/measure-f12/f12.dem --tick 30000 --measure 60 +fps_max 0`

Builds: A = `D:/measure-f12/threaded-sun/` (`849d94c0`), C = `D:/measure-f12/threaded-sun-bound/` (`7057f223`), both
Release. Grid (`grid.ps1`), one run at a time: 01 C-S, 02 C-T, 03 A-T, 04 C-T, 05 C-S, 06 A-T, 07 C-S, 08 C-T, 09 A-T,
10 C-T0, 11 C-T16, 12 C-E, 13 A-E, 14 A-E, 15 C-E (E = empty view, threaded). Question A reads C-S against C-T; D reads
A-T against C-T and A-E against C-E.

Original order (superseded by the grid above): A — gummo S T T S S T (S = `+r_threaded_particles 0`, T = `1`); B — gummo T0 T16 (`TF2VIEW_PARTICLE_THRESHOLD`
0 and 16), each against the nearest T; C — read from every A run's census notes, plus empty ×2. Steady = the per-second
lines after the first five, frame-weighted. Percent of frame = part ms / that run's frame mean.

## Control

- **The switch reached the code:** each census note prints `particle forks so far N`. S runs must read 0 throughout; T
  runs must climb. Equal forks in S and T voids the pair.
- **Same content:** S and T census lines (bursts, trails, model decals) agree at comparable ticks.
- **Same output:** held by tests, not pixels (finding 94 §3: `--shot` differs serial against serial):
  `ParticleEffectsWorkersTests` and `BurstEffectsStepTests.StepOver_APlaybackOnWorkers_IsTheSerialPlayback` (offers,
  census, counters, batch corner bytes, whizzes, frame by frame).
- **Sun instrument's own cost:** two `Stopwatch.GetTimestamp` per call (~20-30 ns each, *estimate*); calls × 50 ns is
  reported beside the sun ms, and the sun ms is not believed below twice that.
- **Sun control:** the sum of callers must equal the total calls in every note (each counted at the same point).
- S runs agree within 15% on `camera particles`, or the set is void.

## Environment

To be filled at run time: commit, CPU load before/after each run, machine (RTX 3060, i5-10600K: 6 physical cores, so the
fork's degree is 5).

## Result

Not yet run.
