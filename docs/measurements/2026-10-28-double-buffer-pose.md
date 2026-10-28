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

## Results

(pending)
