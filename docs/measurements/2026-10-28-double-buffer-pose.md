# 2026-10-28 — the pose into its own half, the wait after it (B581 rung 3, step 1)

What changed (branch `perf/double-buffer-pose`, `815bd95f`): the pose's outputs the draw reads by reference — the world,
undrawn and viewmodel instance lists, their matrix pools, the per-entity skinning matrices and face streams — are kept
twice over, and `PoseHalves` picks the half a pose writes (it moves only when a hand-off happened since the last pose).
The wait for the frame in flight moved from the pose's ENTRY (`FinishDrawBeforePose`) to its END
(`FinishDrawBeforeEffects`), before the particles, decals and lightmaps, which still hand the device what the draw reads
in place. Engine reading and design: finding 98 §9 (rung 3, step 1).

## Predictions, registered before the first run

- **Identity.** Paused `--shot` pairs at tick 30000, empty view and gummo first person: 0 world pixels differ
  (`2026-10-27-empty-view-breakdown/pixdiff.ps1 -WorldOnly`); whole-frame differences only inside the top-bar HUD strip,
  as large as the A/A control's. Falsified by any world pixel.
- **First person, steady window (seconds 6-20):**
  - `camera wait` (now the wait after the pose) falls from A's 0.55-0.57 ms to **under 0.05 ms**: the pose (2-4 ms) is far
    longer than the 0.55 ms of draw that was left at its entry. Falsified above 0.10 ms.
  - No other `queue wait` caller takes it over: each under 0.05 ms in B. Falsified if one rises above 0.10 ms.
  - **Frame: −0.40 to −0.57 ms** (the ceiling is A's whole wait; double-buffered face streams rebuild a still face once
    more per half, and the render thread now draws beside the pose rather than beside the cull). Falsified if the
    difference is under 0.30 ms or the ranges overlap.
  - `queue render thread` within +0.15 ms of A (the draw now competes with the pose for caches and memory).
  - `camera pose` within ±0.15 ms of A.
- **Empty view (the control):** nothing is posed (`posed 0`), so the pose is short and the wait mostly stays (moved to
  after the pose). No claim on the frame; a change under the spread of four runs per build is reported as none.
- **Whole 20 s window:** reported beside the steady window; no separate claim.

## Method

Two Release builds out of the tree: A = `7946c74d` (origin/main), B = `815bd95f` (this branch), each from a
`git archive` at `D:/measure-f12/dbp-{before,after}-src`, built `-c Release` there and copied to `D:/measure-f12/dbp-{before,after}`.
Every run behind the quiet gate (`run.ps1`, as `2026-10-28-pvs-node-prune/run.ps1`), under `run-exclusive.ps1`, at 1280x720:

- first person: `<exe> D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0`
- empty: `TF2VIEW_CAMERA="1613 -2821 154 89 0" <exe> D:/measure-f12/f12.dem --tick 30000 --measure 20 +fps_max 0`

Order (`all.ps1`): shots (A, B, and a second A as the control) per view, then ABBA BAAB first person, then ABBA BAAB
empty, then a second shot pair. Reading: `2026-10-27-empty-view-breakdown/read.ps1` (per-second lines after the first
five frame-weighted; % = part / the steady window's own frame mean).

**Environment note:** TF2 (`tf_win64`, PID 1956) was running on the desktop throughout, left by an earlier agent (a shared
fixture, not quit). It is not excluded by name; the gate's load samples are what admit a run.

## Verifier and sabotage (Debug build, before the timed runs)

`TF2VIEW_QUEUE_VERIFY=1 TF2VIEW_FIXED_FRAME=0.015 TF2VIEW_AUTOPLAY=1`, f12 `--tick 30000 --spectate gummo --first-person
--measure 20 +fps_max 0`, mode `-1` (log: `mat_queue_mode -1: QueuedThreaded on 6 physical cores`). The verifier now also
hashes each instance's light cube and local lights by value (rung 3; `TF2VIEW_FRAME_HASH` does not, so its values stay
comparable with earlier records).

- **Clean, `815bd95f`: `queue verify: 773000 frames checked, 0 changed`**, no exception, exit 0.
- **Sabotage, `PoseHalves` never moving** (`Current ^= 1` → `Current ^= 0`, so every pose writes the half in flight):
  the verifier logged `pose b94c3503…->930ec6e7…` at 30001 and `pose ac1cab2c…->102ea712…` at 30004 (particles, rest and
  cull `same`), then the viewer died with `InvalidOperationException: Collection was modified` in `AddInstances` on the
  render thread. Restored with the inverse edit (`^= 0` → `^= 1`); the working tree matched `815bd95f` after.
- Unit-level sabotage (in the commit): skinning ignoring the half reddened
  `Instances_InTheOtherPoseHalf_LeavesTheFirstHalfsSkinningUntouched`; the world list ignoring the half reddened
  `Pose_AfterAHandOff_WritesTheOtherListAndLeavesTheHandedOneStanding`.

## Environment

RTX 3060, i5-10600K, 2026-10-28 11:37-13:11. Every run passed the quiet gate and exited 0. Load (3 × 1 s, %) before /
after:

| run | before | after | | run | before | after |
|---|---|---|---|---|---|---|
| 01-fp-A | 19 8 16 | 15 9 9 | | 09-empty-A | 14 6 11 | 15 8 15 |
| 02-fp-B | 24 14 13 | 8 15 17 | | 10-empty-B | 7 7 7 | 19 15 10 |
| 03-fp-B | 14 17 12 | 15 10 8 | | 11-empty-B | 8 12 14 | 22 15 15 |
| 04-fp-A | 8 11 11 | 13 5 12 | | 12-empty-A | 12 16 6 | 4 10 9 |
| 05-fp-B | 12 13 14 | 13 21 9 | | 13-empty-B | 14 9 8 | 6 7 17 |
| 06-fp-A | 18 19 14 | 16 16 8 | | 14-empty-A | 12 15 14 | 17 14 8 |
| 07-fp-A | 14 19 12 | 16 18 12 | | 15-empty-A | 10 14 11 | 9 12 7 |
| 08-fp-B | 11 13 10 | 15 12 12 | | 16-empty-B | 14 11 6 | 10 29 15 |

The baseline load of 7-19% is mostly TF2 idling on the desktop (above); no run was voided. Raw output beside this file:
`2026-10-28-double-buffer-pose/NN-*.txt`.

## Results

**Controls.** Every run ended at tick 31347; every first-person run had 18 rebuild reports; first person drew 2,512-2,599 (A)
and 2,737-2,833 (B) frames, the empty view 5,996-6,199. Empty view: `posed 0` in every moment line of all eight runs.

**Identity: held.** `pixdiff.ps1` on the paused `--shot`s at tick 30000 (shots in the session scratch folder, not kept):

| pair | whole frame | world only |
|---|---|---:|
| empty A / B | 17, box (1113,2)-(1119,9) | 0 |
| empty A / B, second pair | 45,478, box (0,0)-(1279,48) | 0 |
| empty A / A (control) | 0 | 0 |
| first person A / B | 28, box (1113,2)-(1119,10) | 0 |
| first person A / B, second pair | 51, box (993,2)-(1119,10) | 0 |
| first person A / A (control) | 41,721, box (0,0)-(1279,48) | 0 |

Every whole-frame difference lies in rows 0-48, the top HUD bar, and the A/A control shows the same (41,721 in the
first-person pair). **Scope:** two cameras at one paused tick, as the prune record; nothing on a moving path.

**First person, steady window (seconds 6-20), ms and % of that run's steady frame:**

| run | frame | `camera wait` | `camera pose` | `camera particles` | `camera setcamera` | render thread (whole window) |
|---|---:|---:|---:|---:|---:|---:|
| 01-A | 6.933 | 0.497 (7.2%) | 3.022 | 1.554 | 0.435 | 2.08 |
| 04-A | 6.893 | 0.509 (7.4%) | 2.979 | 1.546 | 0.433 | 2.10 |
| 06-A | 6.846 | 0.498 (7.3%) | 2.984 | 1.509 | 0.432 | 2.03 |
| 07-A | 6.860 | 0.491 (7.2%) | 2.985 | 1.510 | 0.440 | 2.06 |
| 02-B | 6.417 | 0.000 (0.0%) | 3.068 | 1.507 | 0.428 | 2.08 |
| 03-B | 6.369 | 0.000 (0.0%) | 3.043 | 1.503 | 0.422 | 2.08 |
| 05-B | 6.311 | 0.002 (0.0%) | 3.018 | 1.486 | 0.416 | 2.08 |
| 08-B | 6.458 | 0.000 (0.0%) | 3.059 | 1.541 | 0.428 | 2.13 |

- **Frame: A 6.846-6.933 ms (mean 6.883, 145 fps), B 6.311-6.458 (mean 6.389, 157 fps): −0.49 ms, 7.2% of A's frame,
  ranges apart.** Inside the registered −0.40 to −0.57. Held.
- **`camera wait` 0.491-0.509 → 0.000-0.002 ms.** Under 0.05 as registered. Held. In A it is the wait at the pose's
  entry; in B the wait after the pose (`FinishDrawBeforeEffects`), which finds the frame in flight finished.
- **No other wait took it over.** B's whole-run `queue wait` ledger holds only `FinishDrawBeforeEffects`, on 1-3 of
  20 rate lines, each 0.00-0.08 ms; A's holds `FinishDrawBeforePose` on 16-18 lines (0.18-2.63 ms). Held.
- `camera pose` 2.979-3.022 → 3.018-3.068 (+0.04 ms, inside ±0.15). `queue render thread` 2.03-2.10 → 2.08-2.13
  (+0.02, inside +0.15; this column is the whole-window phase line, not the steady window).
- **Whole 20 s window** (each file's `measured … mean`): A 7.96, 7.82, 7.70, 7.76 ms; B 7.16, 7.22, 7.06, 7.31 —
  **−0.62 ms, ranges apart.** The warm seconds account for the extra: A's `camera wait` there is 0.84-0.95 ms, B's
  0.006-0.011.

**Empty view (the control), steady window:** A 2.833-2.933 ms, B 2.850-2.886 — overlapping: **no difference**, as
registered. `camera wait` is 0.000-0.003 in both: with nothing posed, the frame in flight (render thread 0.32 ms) has
finished before either build's wait.

## Verdict

The pose now overlaps the frame in flight, and `camera wait` is gone: −0.49 ms (7.2%) of the steady first-person frame
on f12, −0.62 ms over the whole window, with the world identical in both views. What is still not the engine's shape:
the wait sits after the pose instead of at the hand-off (`EndFrame`), because the particles, decals, lightmap and HUD
still hand the device what the draw reads in place. On f12 that remaining wait measures 0.00 ms, so moving it buys
nothing here; it is structure, filed in finding 98 §14.

## Gate, and an unexplained hang (NOT merge-ready)

- **Phase 1** (`TF2DEMOSALVAGE_GCOR_ONLY=1`, `-Shared`, at `bd98321f`): twelve assemblies, 0 failed, every one above its
  floor (scene 3387, presentation 1323, rendering 1270, core 2713, animation 5352, content 1859).
- **Phase 2** (`Tf2DemoSalvage.Viewer3D.UiTests`, under the desktop lock): **1 failed, 75 passed, 8 skipped, of 84.**
  `PausedSoundUiTests.Footsteps_HeardWhilePlaying_StopWhenTheDemoIsPaused` timed out after 5 min 2 s, at its 300 s
  `Loading` bound: "opening state applied" never came. Its viewer log (`%LOCALAPPDATA%/Tf2DemoSalvage/
  viewer-20261028-133421-4372.log`, z1800 `--tick 20000 --autoplay +demo_timescale 8 +volume 1`, mode `-1` → 2) holds 38
  once-a-second rate reports through the load, then the first world frame's draw (the render thread's `viewmodel pass
  start: (first frame)` at 13:35:30.805), then **nothing**. The process stayed alive until the test killed it at
  ~13:39:21, so for about four minutes even the background log writer wrote nothing. The suite's shared-fixture viewer
  (`viewer-20261028-133014-40780.log`, also z1800) ran beside it the whole time.
- **What was tried, and what it shows:**
  - the test alone: passed (1 m 11 s);
  - a scripted loop of the same scenario beside a second viewer (f12 autoplay), with a watchdog that, on a stalled
    log, samples the process's CPU and takes a `dotnet-trace` before killing it: **15 of 15 attempts reached "opening
    state applied"**;
  - the loop's first version flagged a "hang" at 97% of one core during the demo's load, before any rate report. That was
    the watchdog's own fault: the load is silent for more than 30 s. It now waits for the first rate report. Not a
    reproduction.
- **Not established:** the cause. Not reproduced in 16 tries. *Suspect:* this branch's new overlap, since the pose now
  runs while the first world frame draws, and the stall came exactly there. The source holds no `lock` in Scene,
  Content, Animation or Render that the pose could hold across a drain. The candidates left are:
  - a non-concurrent collection touched by both threads, which spins (rung 2's `BspVisibility` row cache was one);
  - a process-wide stall: a log writer that stopped too says more than one thread stopped.

  **No baseline control was run**, so whether `7946c74d` also hangs here is unknown.

### Follow-up, 2026-10-28 14:00-16:00: controls, a stack instrument, and five more suites

**Asked for:** a baseline control, a stack of the hung viewer, and the root cause fixed red-first. **What was done:**

- **Baseline control** (`7946c74d`, its own Debug build in `D:/measure-f12/dbp-before-src`): the test alone, 15 times, under the desktop lock. **15 of 15 passed** (1 m 5 s to 1 m 20 s). This is **not** a discriminating control: the branch also passes alone (1 time by the gate's own rerun, and 35 scripted attempts, below). The hang has only been seen inside the full suite, so A's isolated passes say nothing about A inside the suite.
- **The branch inside the suite again**, four more full phase-2 runs with a watchdog armed: **76 passed, 8 skipped, 0 failed of 84, every time** (6 m 37 s to 7 m 2 s each). The run that failed took 11 m 8 s. The branch's phase-2 record is now **4 green of 5**.
- **The scripted loop, corrected.** Its first 15 attempts lacked the harness's `+developer 1 +cl_game_folder_ask 0 +cl_screenshot_folder`, so the first world draw did not log every model's first draw and was shorter. The overlap is the window this branch opened. Rerun with the harness's exact arguments, beside a second z1800 viewer at `developer 1` (the suite's shared fixture): **20 of 20 reached "opening state applied"**. Their logs carry `viewmodel pass start: (first frame)`, so the conditions matched.
- **No stack was captured, because nothing hung.** The watchdog sampled CPU and took a 5 s `dotnet-trace` (sample profiler) of every viewer whose log fell silent for 30 s after its first rate report. It flagged four viewers, one per suite. In each one the stack (`speedscope`, the stack open at the trace's midpoint) showed:
  - the main thread in `ViewerSession.Idle` → `Thread.Sleep`;
  - `MatQueue` in `QueuedRenderThread.Run` waiting for work;
  - the log writer waiting on its channel.

  That is the suite's shared viewer sitting idle, and it **proves the instrument reads thread stacks**. It is not the hang. `createdump` takes no PID in .NET 10 (*"The pid argument is no longer supported"*), so a full dump needs `dotnet-dump`, a download not made.
- **The hung run's own evidence is gone.** The viewer's log retention removed `viewer-20261028-133421-4372.log`, and the capture folder was overwritten by later runs. What is quoted above was read before that. Two checks were made from the code instead:
  - *An exception would have been logged.* `Program.Main` sets `UnhandledExceptionMode.CatchException`, and its `ThreadException` handler logs the stack and flushes. A render-thread failure rethrown at `FinishDrawBeforeEffects`, the shape the `PoseHalves` sabotage produced, therefore leaves a log line and does not open a dialog. The quoted tail had none. So "a wrong half threw" does not fit a silent log, though the full log can no longer be searched to rule it out.
  - *No lock the pose could hold across a drain.* No `lock` or semaphore exists in Scene, Content, Animation or Render outside the queue and the pass profiler.
- **The instrument the next occurrence needs, built** (`QueuedRenderThread.StallAfter`, `Stalled`, `Stage`):
  - `Lock` now waits in two parts. After 5 s it logs once, `queue stall: <caller> has waited 5 s on the frame in flight, whose last finished pass is <RenderPass>`, then goes on waiting.
  - The draw marks each pass as it finishes (`Device3D.Passed`, beside the existing `PassProfiler` marks), and a frame starts at -1.
  - Behaviour is unchanged: the wait still ends only when the frame does.
  - Tests, red first: `Lock_PastTheStallBound_ReportsOnceTheCallerAndTheFramesLastPass` failed against a stub with the members and no report, "nothing reported the stall, so nothing opened the frame's gate". It passes with the report. The control is `Lock_WithinTheStallBound_ReportsNothing`.
- **No fix was made.** Without a reproduction no change can be shown red-first against the real failure, and a speculative one would be a guess.

**Gate with the instrument in.** Build: 0 warnings, 0 errors. Phase 1: twelve assemblies, 0 failed, rendering 1272 (two
new tests). Phase 2: **76 passed, 8 skipped, 0 failed of 84** (6 m 48 s). No viewer log of the 49 newest holds a
`queue stall` line.

**Status: not explained, and not merge-ready on that account.** 1 hang in 6 full suites on the branch, 0 in 35 isolated
or scripted branch runs, 0 in 15 isolated baseline runs. The suspect is still this branch's overlap: the pose of frame 2
was at 13:35:30.677 while the render thread drew frame 1 until 30.805, then silence. Unproved. The next occurrence will
log a `queue stall` line naming the caller and the pass the frame in flight had reached.
