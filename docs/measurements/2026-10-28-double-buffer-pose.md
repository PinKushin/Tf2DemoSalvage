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

## Gate, and an unexplained hang (merge-ready by the owner's decision, D239; the hang is a known flake, B609)

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

### The owner's hypothesis: "the footsteps test isnt waiting on the demo to load"

Checked against what survives: the phase-2 console output of the failing run (session scratch `gate2.txt`), the
quoted lines of its viewer log above (the log itself is gone), and `PausedSoundUiTests.cs`.

1. **Which wait timed out: the first, the load.** The stack ends at `PausedSoundUiTests.cs:line 65`. That is
   `Retry.WhileFalse(() => viewer.Count("opening state applied") > 0, Loading /* 300 s */, …, throwOnTimeout: true)`,
   and the 5 m 2 s run time is that bound. The 30 s footstep wait (`:66`), the Play-button `Activate` (`:70`) and the
   pause and report waits (`:72-83`) never ran.
2. **The test was waiting on the demo to load, and the viewer stopped before finishing.** Its log reached the first
   world frame at 13:35:30 (frame 2's pose at `.677`, the render thread's `viewmodel pass start: (first frame)` at
   `.805`). After that it wrote nothing, not even a rate report, until the test killed it at about 13:39:21.
   - In a good run the next lines are the opening skip (`skip: to 20000 …`) and then "opening state applied", about
     4 s after the first world frame (`viewer-20261028-134950-12092.log`: first draw 13:50:49.849, applied 13:50:53.761).
   - So the load did not need longer. It stopped making progress. A load that is merely slow keeps writing its rate
     report each second, as all 38 seconds of this run's load did.
3. **Does "opening state applied" come before the load is complete?** It is logged after the opening seek's skip. In
   every good log the playing frames and the first footstep follow it within the test's 30 s, so that ordering has
   not failed the test in any run seen. It could matter for the later waits, but the failing run never reached them.
   Whether it is the right "loaded" signal is a fair question for those waits; it is not this failure.

**One thing the output does show:** the harness logs memory before each test, and before this one it read **"physical
3987 of 32632 MB free, memory load 87%"**. Passing tests do not print it, so there is no comparison from the green
runs. *Not established*: whether memory pressure (two z1800 viewers at `developer 1`, TF2 resident) stalled the viewer.

**So no test fix was made.** The evidence points away from a test synchronisation defect: the test waited 300 s on the
right condition, and the viewer went silent. The like-for-like control below decides between this branch and the machine.

### The like-for-like control (item A): full suites, baseline against branch, 16:48-18:04

The whole UI suite (`Tf2DemoSalvage.Viewer3D.UiTests`, 84 tests), run alternately on the two builds under one desktop
lock, with the stall watchdog armed throughout:

- A is `7946c74d`, its own Debug build in `D:/measure-f12/dbp-before-src`.
- B is the branch at `6991bf4c`, the review fixes and the stall report in.
- Script: session scratch `abloop.ps1`. A1, B1, A2, B2, … B6, then one replacement A.

| run | A (baseline) | B (branch) |
|---|---|---|
| 1 | 76 passed, 0 failed | 76 passed, 0 failed |
| 2 | 76, 0 | 76, 0 |
| 3 | 76, 0 | 76, 0 |
| 4 | 75, **1 failed**: `Activate_AMenuItemWithAnotherAppInFront_TogglesItWithoutTheCursor` (2 s) | 76, 0 |
| 5 | **TAINTED**: 75, 1 failed, `StartupPicker_AFolderThatIsNotTf2_IsRefusedAndNothingIsSaved` (30 s) | 76, 0 |
| 6 | 76, 0 | 76, 0 |
| 7 (replacement for A5) | 76, 0 (18:07-18:13) | — |

- **The hang: 0 of 13 runs** (0 of 6 clean A, 0 of 6 B). `Footsteps_HeardWhilePlaying_StopWhenTheDemoIsPaused` passed in
  every run, the tainted one included.
- **A5 is tainted by human input, kept visible, not counted.** The owner touched the desktop during a game-folder test
  (a scroll wheel stopped the test's typing, and he picked a folder by hand). The logs place it in A5:
  - that run's non-TF2 viewer (`viewer-20261028-174205-31672.log`) waited 29 s and was given `C:\$GetCurrent`, not the
    test's own `…\Pictures` folder;
  - the same test's viewer in B4 (`viewer-20261028-173608-13728.log`) took 3 s on its `Pictures` folder.

  The game-folder test failed in A5, and Footsteps passed in it.
- **A4's failure counts as a real baseline flake.** A focus-related menu test failed in 2 s. Nothing in its timestamps
  places the owner there: the owner's own account was of the game-folder test, which is A5.
- **The watchdog** flagged one viewer (16:50:35, the suite's shared viewer at its start, as in every earlier flag). No
  `queue stall` line was written by any B viewer.

**Load time (the owner's second hypothesis: "the load time might be longer now, so it might be timing out").** These are
the paused-sound test's own viewers, harvested before log retention removed them (scratch `pslogs/`, `loadtimes.ps1`).
The load is the seconds from the log's first line (process start) to "opening state applied", the test's 300 s
`Loading` wait. The step is the seconds from there to the first `player/footsteps/` line, its 30 s wait.

| build | runs | load: min / median / max (s) | step: min / median / max (s) |
|---|---:|---|---|
| A, in suite | 6 | 54.7 / 60.6 / 64.3 | 0.3 / 0.3 / 0.4 |
| B, in suite | 6 | 53.6 / 59.3 / 63.1 | 0.3 / 0.3 / 0.3 |
| B, harness-exact scripted, beside a second viewer | 16 | 52.8 / 60.2 / 65.2 | 0.3 / 0.3 / 0.5 |
| B, the failed run | 1 | (first world frame at 69 s, then silent) | — |

- The 300 s bound is 4.6× the slowest load seen.
- The 30 s footstep bound is 60× the slowest step.
- The pause and report waits are 30 s on once-a-second reports.

**No wait is within the factor of 3, so the test's clocks were not changed.** The two builds' loads are the same
within their spread. *Not done*: the comparison with an older build. B607's 0.5-0.6 s of load work cannot bring a
65 s load near 300 s.

### Review fixes, 2026-10-28 (`6991bf4c`): two reviews, items B-F

- **B, the count could be omitted.** `MomentScene.Pose(…, handOffs = 0)` let a seek's and a replay's pose
  (`FramePresenter.Seek.cs`, `PoseNow(ViewFrustum.Nothing)`) pass 0. Against a real device count that flips onto the
  half handed. It was safe only because those paths drain first.
  - *Fixed:* the count is no longer an argument. It is `MomentScene.HandOffs`, a source `ViewerPresenter` points at the
    device the frame last posed over (`_handingDevice`, set in `PoseOver`), so every pose reads the same count.
  - *Without a device* it is null: one half, and nothing in flight.
  - *Test:* `UploadCameraOver_OverADevice_PointsThePosesHandOffCountAtThatDevice`. Sabotage: not setting the device
    reddened it.
- **C, `SkinningOf` wrote a pose half.** Between frames that is the half in flight; it was drained only by call order.
  - *Fixed:* it builds into `_queriedSkinning`, its own buffers. `ModelDecals.Add` projects from the matrices at once
    and keeps none (*read*).
  - *Test:* `SkinningOf_AfterThePose_BuildsIntoItsOwnBuffersNotTheDrawnOnes`. Sabotage: writing `_skinning[PoseHalf]`
    reddened "the query rewrote the drawn skinning matrix".
- **D, tests that could not fail, added and each sabotaged red:**
  - the face stream per half: `Pose_AfterAHandOffMidLaugh_LeavesTheHandedFaceStreamStanding`, on the install. A shared
    `_lastFace[0]` reddened "the next pose wrote the handed stream". The other suggested mutant, `[1 - PoseHalf]`, is
    *equivalent*: two dictionaries swapped are still two, and no test can or should tell them apart;
  - the undrawn and viewmodel lists per half: `Pose_AfterAHandOff_WritesTheOther{Undrawn,Viewmodel}List…`. Each `[0]`
    reddened;
  - the stall test, rewritten so the frame is held on its own gate. The report must arrive while it is held, the wait
    must still run after the report, and it must end when the frame is let go. An early return after the report reddened
    "the report ended the wait while the frame was still held".
- **E, answered from the code:**
  - *A map change while playing:* `ViewerPresenter.ClearMap` calls `_device?.Queue.Lock()` first, then
    `_levels.Shutdown()`, which runs `MomentScene.LevelShutdownPreEntity` and clears both halves. It is the only caller.
    So a frame in flight is drained before either half is cleared. *Read, not tested*: `ClearMap` needs a real `Device3D`.
  - *Does any gate fail on a logged warning?* No. No UI test, `build/` script or workflow counts `WARN` lines. The
    viewer's log is read by the UI tests for specific strings only. The stall report stays a warning.
- **F:**
  - the stale comment in `ViewerSession.Frame.cs` (the flight path's wait) is corrected;
  - **the raw run outputs beside this file were NOT removed**: this agent may not delete files. The 52 `.txt`/`.err`
    files under `2026-10-28-double-buffer-pose/` (26 of them empty `.err`) are for the lead to `git rm` if wanted. The
    `.md` and the two scripts are the record; the empty `.err` files carry nothing;
  - **the stall report stays.** It costs one timed wait, and it reports only a wait of over 5 s
    (`QueuedRenderThread.StallAfter`, settable at construction), which no healthy frame has: the slowest seen here, a
    first-use frame, logged `SLOW FRAME 194 ms` (`viewer-20261028-134950-12092.log`, one of the scripted runs above). It is the instrument that would have explained this hang, and the next one anywhere.
    *Removal condition:* it goes, or its bound rises, if it ever logs on a run that did not hang.

**Gate with the instrument in.** Build: 0 warnings, 0 errors. Phase 1: twelve assemblies, 0 failed, rendering 1272 (two
new tests). Phase 2: **76 passed, 8 skipped, 0 failed of 84** (6 m 48 s). No viewer log of the 49 newest holds a
`queue stall` line.

**Superseded by the like-for-like control above** (13 more suites, 0 hangs on either build; verdict at the end). The
paragraph that follows was the state before it.

**Status then: not explained, and not merge-ready on that account.** 1 hang in 6 full suites on the branch, 0 in 35 isolated
or scripted branch runs, 0 in 15 isolated baseline runs. The suspect is still this branch's overlap: the pose of frame 2
was at 13:35:30.677 while the render thread drew frame 1 until 30.805, then silence. Unproved. The next occurrence will
log a `queue stall` line naming the caller and the pass the frame in flight had reached.

## The gate, once, on the final code (`6991bf4c` plus records), 18:13-18:27

- Build: 0 warnings, 0 errors.
- **Phase 1: NOT green.** Ten assemblies passed at or above their floors: core 2713, cli 75, logging 37, fonts 5,
  animation 5352, scene 3390, audio 383, presentation 1324, content 1859, corpus 236. Viewer, which comes after
  rendering, was not reached.
- **Rendering's run aborted.** It was a native access violation (`0xC0000005`) in `ID3D11DeviceContext.Map`, under
  `WorldRenderer.DrawScreenOverlay` ← `OffscreenTarget.DrawScreenOverlay` ←
  `CloakRenderTests.DrawScreenOverlay_JarateOverAUniformFrame_KeepsRedAndDarkensBlue`, after 129 of its tests had
  passed.
  - That path is an offscreen target with its own device. It has no queue, no `Device3D` and no pose halves, and this
    branch's diff does not touch it.
  - *Control:* `CloakRenderTests` (9 tests) run 5 times on the baseline and 5 times on the branch: **10 of 10 passed**,
    with no crash.
  - The same assembly passed whole (1272) in the 16:10 gate on this branch.
  - *Filed as a separate defect, not explained:* a crash in D3D11 `Map` on the overlay path, seen once.
- **Phase 2: 76 passed, 8 skipped, 0 failed of 84** (6 m 8 s).

## Verdict on the hang (after the like-for-like control)

- **Branch, full suites: 1 hang in 12.** The one was the first suite of the day: 11 m 8 s, memory load 87% before the
  test. The next 11 ran 5 m 53 s to 7 m 2 s with none.
- **Baseline, full suites: 0 in 6 clean**, plus one other flake (A4's menu test) and one run tainted by the owner (A5).
- **Branch, isolated or harness-exact scripted runs: 0 in 36.**
- **The load-time hypothesis is ruled out by measurement.** Loads are 53-65 s on both builds against a 300 s bound.
- **The test-synchronisation hypothesis is ruled out by the failure's own stack.** The test failed on its first, 300 s,
  load wait, while the viewer had stopped logging four minutes earlier.

**What this supports:** at one occurrence in 12, nothing distinguishes the branch from the baseline. With a true
rate of 1 in 12 on both, a clean 0 in 6 for the baseline is likely, about 0.59 (*arithmetic*). So the control cannot
say the hang is the branch's, and it cannot say it is not. **Not established:** the cause. The stall report is in place,
so a recurrence will name the waiting caller and the last pass of the frame in flight.

**Status: merge-ready, by the owner's decision (D239, 2026-10-28).** His words: *"merge it, file the hang as a flake, i
think it was probably just a slow load because tf2 was open or somethign"*. The hang is filed as a known flake, B609,
with the condition that reopens it. The evidence neither confirms nor refutes his reading. The rendering crash in the
final gate's phase 1 is filed apart, as B610.
