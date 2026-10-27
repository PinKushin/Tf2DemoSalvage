# Measurement 2026-10-27: the bone clock once a frame, and `cl_threaded_bone_setup` on that base

**Status: measured 2026-10-27.** The plan above the Environment was committed (`7ada01c6`) before any run.

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

RTX 3060, i5-10600K (6 physical cores, pool degree 5); builds as in the Method; 2026-10-27 03:06-03:55, mode 2 for the
grid. Every run passed the gate first time and all eight ran in order. Load before / after (%): 01 12 12 5 / 16 18 14;
02 11 16 21 / 10 12 12; 03 12 18 9 / 38 35 40; 04 10 9 13 / 17 26 9; 05 13 6 20 / 20 15 18; 06 20 23 21 / 17 13 24;
07 16 20 17 / 17 22 13; 08 20 21 7 / 16 13 16. **Machine control held:** `sound` 0.15-0.16 and `between` 0.32-0.34 in
every run. No run discarded.

## Result

Raw output: `2026-10-27-bone-clock-and-threaded-setup/0N-*.txt`; moment and rate lines by the last file's `read.ps1`;
attachments from each run's `particle cost a frame` line. ms a frame (per rebuild = per frame).

| run | frame | setup | eligible (builds) | prepose (built) | built draw | camera pose | camera particles | attachment builds, ms | search |
|---|---:|---:|---:|---:|---:|---:|---:|---|---:|
| 03 O | 9.17 | 0.634 | 0.545 (6.18) | — | 37.3 | 3.51 | 1.77 | 7.4, 0.35 | 0.55 |
| 04 O | 9.15 | 0.642 | 0.546 (6.23) | — | 37.6 | 3.54 | 1.78 | 7.5, 0.35 | 0.56 |
| 01 NS | 8.65 | 0.534 | 0.435 (4.77) | 0 (0) | 36.6 | 3.36 | 1.54 | 3.5, 0.18 | 0.32 |
| 06 NS | 8.64 | 0.520 | 0.427 (4.64) | 0 (0) | 35.5 | 3.38 | 1.54 | 3.5, 0.18 | 0.32 |
| 07 NS | 8.72 | 0.557 | 0.456 (4.78) | 0 (0) | 36.6 | 3.42 | 1.55 | 3.5, 0.18 | 0.33 |
| 02 NT | 8.55 | 0.384 | 0.448 (4.75) | 0.225 (4.06) | 36.7 | 3.27 | 1.53 | 3.5, 0.18 | 0.33 |
| 05 NT | 8.53 | 0.389 | 0.445 (4.73) | 0.223 (3.99) | 36.4 | 3.26 | 1.52 | 3.5, 0.18 | 0.32 |
| 08 NT | 8.68 | 0.386 | 0.456 (4.78) | 0.230 (4.09) | 36.7 | 3.35 | 1.56 | 3.5, 0.18 | 0.33 |

N's attachment line adds, in every run: `0 rebuilt after this frame's first; roots built 2.1, eligible 2.1 in
0.171-0.179 ms`.

Controls:

- Clock fix reached the code: N prints the roots columns, O does not. Switch: NS bone forks 0 and `prepose 0 over 0`
  throughout; NT forks climb ~55 → ~5,300.
- **Same work NS against NT: held** — eligible builds 4.64-4.78 against 4.73-4.78; `built … draw` 35.5-36.6 against
  36.4-36.7 (means 36.3 and 36.6, 0.9%).
- Identity, pose part (`compare.ps1`): h1-NS against h3-NT **equal on all 220 common labels**; h2-NS differs from both on
  16 early labels (30002-30017) — the run-to-run control's own noise; cull 0 differ in every pair.
- *Added after the run, from files already on disk* (advisor review): **does the clock fix change the output?** The last
  file's h1-S and h2-S (old build, same command, 1280x720) against h1-NS and h2-NS: pose differs on 29, 21, 13 and 5 of
  ~211 labels, **all at or before 30030**, equal on every label after it; cull 0 differ. The old S-S pair itself differed
  at 30023-30030 and the new one at 30002-30017, so the opening ~30 ticks are outside what this instrument can judge, and
  from tick 30031 on the new base draws the bones the old one did.

## Verdict

**A — the clock fix: −0.43 to −0.53 ms a frame (4.7-5.8% of O's 9.16), ranges well apart.** Attachment builds fall 7.4-7.5
→ 3.5 a frame and their ms 0.35 → 0.18; the burst search, which holds the queries, 0.55-0.56 → 0.32-0.33; eligible
builds 6.2 → 4.6-4.8 (the next-frame rebuild the rolled mask caused); `setup` 0.63-0.64 → 0.52-0.56; `camera
particles` 1.77-1.78 → 1.54-1.55. Prediction **falsified twice**: attachment builds did not fall under 1 (3.5 remain,
none a rebuild — entities nothing drew), and the frame fell by more than the 0.2-0.4 predicted.

**B — the fork on that base, same work: `setup` −0.13 to −0.17 ms (1.5-2.0% of the frame); the frame −0.04 to −0.11 ms
in each NS/NT pair, ranges touching (8.64-8.72 against 8.53-8.68).** `setup` falls at the bottom of, and partly below,
the predicted 0.15-0.30: **partly falsified**. The frame is **inconclusive**. With ~4 roots built a frame by the pre-pass,
the fork's ceiling is what the measurement shows: `prepose` 0.22-0.23 ms for work the meter sums at ~0.45.

**C — confirmed on the new base**: NT's pose equals an NS run's on every label, including the opening ticks.

**What changes:** the clock fix stands on its own (parity, 0.43-0.53 ms). The fork stays default off (`cl_threaded_bone_setup
0`, the engine's): ~0.15 ms of `setup`, frame inside the noise, for a switch Valve ships development-only. What the
attachment queries still build — 2.1 eligible roots a frame, 0.17-0.18 ms — is entities nothing drew this frame, which
the engine would pre-pose from last frame's list (`g_PreviousBoneSetups` takes every caller's, c_baseanimating.cpp:2897);
adding them to the pre-pass is the coordinator's amendment and is the next step if the fork is wanted.
