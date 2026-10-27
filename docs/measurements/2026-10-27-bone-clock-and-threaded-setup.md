# Measurement 2026-10-27: the bone clock once a frame, and `cl_threaded_bone_setup` on that base

**Status: plan.** Written and committed before any run, with the build it measures (`6ad02f30`).

## Observation

1. `2026-10-27-threaded-bone-setup.md` (build `47db74a7`) was inconclusive on the frame and its same-work control failed:
   with the pre-pass the eligible builds were 4.7 a frame against 6.1-6.4 without. Its particle cost line read
   `attachments 8.9 queries, 7.5 skeletons built, 0 rebuilt …, 0.35 ms` (`08-T.txt`), and the coordinator measured the
   same ~0.35 ms on the main thread.
2. Reading the source put both on the bone clock: the engine bumps it once a frame (`InvalidateBoneCaches`,
   cdll_client_int.cpp:2163), ours bumped on every pass. `6ad02f30` advances it once, at the world pass; `013b1701`
   fixed the rebuild counter, which could only read 0.

## Question

- A. What does the clock fix change on f12 gummo first person, `cl_threaded_bone_setup 0`: attachment builds a frame and
  their ms, `setup`, builds, `camera pose`, frame — old build (O, `47db74a7`) against new (N, `6ad02f30`)?
- B. On the new base, with the same work, what does the fork take off: `setup`, `camera pose`, frame, N with
  `cl_threaded_bone_setup 0` (NS) against `1` (NT)? Same-work control: builds equal.
- C. Identity on the new base: the frame hash's pose part NS, NS, NT, matched by tick.

## Prediction

- A: attachment builds fall from ~7.5 to under 1 a frame and their ms from ~0.35 to under 0.05; `built` falls by about
  as many; frame 0.2-0.4 ms lower. Falsified if attachment builds stay above 3 a frame.
- B: eligible builds equal NS/NT within 3%; `setup` 0.15-0.30 ms lower under NT; frame lower by about that, possibly
  inside the spread.
- C: pose equal NS-NT wherever NS-NS agree.

## Method

Release builds: O = `D:/measure-f12/threaded-bones/` (`47db74a7`, from the last file), N = `D:/measure-f12/bone-clock/`
(`6ad02f30`, `run-exclusive.ps1 -Shared dotnet build managed/Tf2DemoSalvage.Viewer3D -c Release`). One viewer at a time
under `run-exclusive.ps1`, each timed run behind the quiet gate of `run.ps1` (the last file's, with `-Exe`): three
`Win32_Processor.LoadPercentage` samples under 25%, no other `tf2demoview`/`dotnet`/`MSBuild`/`testhost`; the grid stops
if a gate is not met.

- timing, 60 s each, `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 <exe> D:/measure-f12/f12.dem --tick 30000
  --spectate gummo --first-person --measure 60 +fps_max 0 +r_threaded_particles 1 +cl_threaded_bone_setup <0|1>`, mode 2.
  Order (`grid.ps1`): 01 NS, 02 NT, 03 O, 04 O, 05 NT, 06 NS, 07 NS, 08 NT.
- identity (`identity.ps1`): `TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_FRAME_HASH=1 TF2VIEW_FIXED_FRAME=0.015`, mode 0,
  `--measure 6`: h1 NS, h2 NS, h3 NT; compared with the last file's `compare.ps1`.

Reading: the last file's `read.ps1`, plus each run's one `particle cost a frame` line (the whole window).

## Control

- **The clock fix reached the code:** N's particle cost line prints `roots built`/`eligible` (O's does not).
- **The switch reached the code:** NS bone forks 0 throughout, `prepose 0 over 0`; NT forks climb.
- **Same work** (B): eligible builds and `built … draw` NS against NT within 3%, or B is void.
- **Machine:** phases nothing here touches (`sound`, `between`) agree within 10% across all runs, or the runs outside are
  set aside and named.
- **Hash control:** NS-NS; a label they disagree on says nothing NS-NT.

## Environment

## Result

## Verdict
