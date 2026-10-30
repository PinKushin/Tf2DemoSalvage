# Measurement 2026-10-26: `r_threaded_particles` on current main, and the sun over the whole frame

**Status: measured 2026-10-26.** The plan above the Environment was written and committed (`ff7a70dc`) before the grid ran.

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

RTX 3060, i5-10600K (6 physical cores, so the fork's degree is 5), Release builds as named above, 2026-10-26 20:08-21:26,
`mat_queue_mode` default (-1, so 2 here). Every run passed the gate first time; load before / after (3 × 1 s, %):

| run | before | after | run | before | after | run | before | after |
|---|---|---|---|---|---|---|---|---|
| 01 C-S | 15 15 17 | 13 8 14 | 06 A-T | 13 7 14 | 30 14 16 | 11 C-T16 | 14 14 22 | 15 13 11 |
| 02 C-T | 8 15 12 | 20 23 22 | 07 C-S | 14 14 11 | 25 18 19 | 12 C-E | 19 17 19 | 23 16 16 |
| 03 A-T | 23 24 20 | 18 16 22 | 08 C-T | 21 20 23 | 22 17 15 | 13 A-E | 18 12 9 | 20 13 14 |
| 04 C-T | 18 13 5 | 22 13 18 | 09 A-T | 15 11 12 | 32 17 17 | 14 A-E | 16 22 14 | 25 22 26 |
| 05 C-S | 20 15 20 | 20 6 12 | 10 C-T0 | 21 23 12 | 24 16 12 | 15 C-E | 14 13 20 | 32 18 29 |

No run was discarded. `00-probe` (15 s, A, before the grid) is the observation that added question D and is not in the
tables.

## Result

Raw output: `2026-10-26-threaded-particles-and-sun/NN-*.txt`. Steady = the 53 per-second lines after the first five,
frame-weighted; `frame` is the run's `measured … mean`. ms a frame.

| run | frame | camera | camera particles | bursts/step | sun | sun calls | forks at end |
|---|---:|---:|---:|---:|---:|---:|---:|
| 01 C-S | 8.93 | 6.67 | 1.81 | 0.275 | 0.012 | 1.53 | 0 |
| 05 C-S | 9.03 | 6.77 | 1.85 | 0.282 | 0.012 | 1.53 | 0 |
| 07 C-S | 8.98 | 6.70 | 1.79 | 0.275 | 0.013 | 1.53 | 0 |
| 02 C-T | 8.74 | 6.51 | 1.71 | 0.196 | 0.012 | 1.52 | 6,439 |
| 04 C-T | 8.74 | 6.52 | 1.71 | 0.197 | 0.013 | 1.52 | 6,432 |
| 08 C-T | 8.94 | 6.65 | 1.73 | 0.204 | 0.012 | 1.53 | 6,296 |
| 03 A-T | 9.37 | 7.01 | 1.73 | 0.199 | 0.457 | 1.54 | 6,009 |
| 06 A-T | 9.51 | 7.17 | 1.78 | 0.203 | 0.465 | 1.55 | 5,923 |
| 09 A-T | 9.55 | 7.23 | 1.77 | 0.207 | 0.464 | 1.55 | 5,894 |
| 10 C-T0 | 9.11 | 6.79 | 1.80 | 0.205 | 0.012 | 1.54 | 6,536 |
| 11 C-T16 | 8.90 | 6.61 | 1.76 | 0.205 | 0.012 | 1.53 | 4,282 |
| 12 C-E | 3.55 | 1.83 | 1.25 | 0.181 | 0 | 0 | 15,688 |
| 15 C-E | 3.53 | 1.80 | 1.23 | 0.177 | 0 | 0 | 15,776 |
| 13 A-E | 3.63 | 1.81 | 1.24 | 0.184 | 0 | 0 | 15,356 |
| 14 A-E | 3.60 | 1.80 | 1.23 | 0.180 | 0 | 0 | 15,510 |

Sun control: in all 899 `sun per frame` notes the callers sum to the total (within the 0.01 rounding). The light cache
is nearly all of it; static props are non-zero in 173 notes and direct `SunAt` (impacts) in 12, each a few hundredths.

- **A, the fork-join** (C-S against C-T): `bursts/step` 0.275-0.282 → 0.196-0.204, **−0.07 to −0.09 ms (0.8-1.0% of
  the frame)**, ranges separate. `camera particles` 1.79-1.85 → 1.71-1.73, −0.06 to −0.14 ms. Frame 8.93-9.03 → 8.74-8.94:
  the ranges touch (08 C-T), so the frame difference, about −0.1 to −0.2 ms, is not established by itself. **Matches the
  prediction and finding 94's 0.08 ms.** Switch control held: S forks 0 throughout, T climbs; census lines agree.
  Allocation (each run's first `gc:` line): S 0.81-0.83 MB a frame, T 0.81-0.82; gc paused 0.15-0.16 against 0.16 ms a
  frame — the fork's per-call `Parallel.For` allocation does not show. The main thread's own bytes fall 734-736k → 729-731k
  a frame, since the steps' allocations (`Carried`) now land on workers.
- **B, the threshold:** 0, 4 and 16 give `bursts/step` 0.205, 0.196-0.204, 0.205 — inside one another's spread. Threshold
  16 forks a third fewer lists (4,282 against ~6,400) for the same step, so the lists f12 makes are mostly above 16 and
  the threshold does not matter at this size. **4 is kept**; it is not measured to be better than 0 or 16.
- **C, the sun over the whole frame** (A runs): **1.54-1.55 calls a frame, nearly all light cache builds, costing 0.457-0.465
  ms a frame (4.9% of A's 9.4-9.6 ms frame), about 0.30 ms a call.** B594's census was right about the count and wrong to
  call it cheap. The timer's own cost, 2 clock reads × 1.55 calls (≈ 0.0001 ms, *estimate*), is far below the reading.
  Empty view: 0 calls (control: nothing in view misses the cache).
- **D, the bounded terrain sweep** (A-T against C-T): `sun` 0.457-0.465 → 0.012-0.013 ms with the same 1.52-1.55 calls,
  **−0.45 ms (−4.8% of the frame), 0.008 ms a call, 37× cheaper.** `camera` 7.01-7.23 → 6.51-6.65 (−0.5 ms) and the frame
  9.37-9.55 → 8.74-8.94 (**−0.6 ms, −6.5%**), ranges well apart. *Interpolated, not timed:* the part above the sun's
  0.45 is the other `TraceBrushOnly` callers, which take the same bound — the light cache's own local-light `reaches`
  traces in the same entry builds (`LevelLighting`, `MASK_OPAQUE`), the soundscape line of sight, beams, impacts. Empty
  view: frame 3.60-3.63 → 3.53-3.55, −0.06 ms (−1.7%), ranges separate, with no sun call: other traces again, by the
  same interpolation.
- **Production equivalence on the real map:** probe `terrain-bound` on `cp_process_f12.bsp`, 50,000 traces (short and
  8,192-unit rays, boxes 0/2/16, and the 57,016-unit sun trace from seeded points, `MASK_SOLID` and `MASK_OPAQUE`):
  **0 differ** from the whole-ray compose in any `BspTrace` field, with terrain winning 375 (the control). With the bound
  sabotaged (the box's x end halved) the same probe reports 29 differ; restored.

## Verdict

Mixed; each prediction separately. **A** (fork-join): confirmed on `bursts/step`, which fell 0.275-0.282 to 0.196-0.204 ms
(-0.07 to -0.09, predicted 0.05-0.10), ranges separate, no part rose; the frame piece is inconclusive: predicted under 0.1
ms, observed about -0.1 to -0.2 with touching ranges, so not established. **B** (threshold): confirmed, 0, 4 and 16 give
0.205, 0.196-0.204 and 0.205 ms, no difference within spread; 4 is kept. **C** (sun calls): falsified on the cost. The
calls matched (1.54-1.55 a frame, nearly all light cache builds; empty view 0), but the predicted total of 0.05 ms or less
was 0.457-0.465 ms a frame (4.9% of the frame, about 0.30 ms a call), so the profile's 5.8% was real, not attribution bias.
**D** (bounded sweep): confirmed, `sun` fell to 0.012-0.013 ms (predicted under 0.03) with the same calls, 0.008 ms a call
against 0.30; the starting point was higher than predicted (0.46, not 0.15-0.3), and the frame fell 0.6 ms (-6.5%), more
than the sun's own 0.45, the excess being interpolated to other traces taking the same bound, not timed.

Verdict written 2026-10-29 after the fact from the Result above; the run itself was not repeated.
