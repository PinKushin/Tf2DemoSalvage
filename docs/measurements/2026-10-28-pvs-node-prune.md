# 2026-10-28 — the world walk pruned by the PVS (B259)

What changed: `WorldVisibility` stamps the PVS-visible leaves and their ancestors once per change of the eye's cluster
(the engine's mark pass, read from engine.dll — RISKS B259) and the walk returns from an unstamped node before its box
test, where it used to descend every node the frustum kept and test the PVS per leaf. The listed leaves and reached
surfaces are unchanged on cp_process and cp_badlands (`Surfaces_ForAPvsPrunedWalk_AreTheUnprunedWalksInTheSameOrder`);
node steps fall by about three quarters there (40,848 of 162,512, 49,515 of 187,710 over 175 views).

**Headline (corrected 2026-10-28 after review).** In first person the pass cost fell 0.36 ms (`camera setcamera`
0.761-0.770 → 0.404-0.415), **but 0.30 ms of it became waiting**: `camera wait` rose 0.253-0.263 → 0.554-0.568 ms. That
wait is `Device3D.FinishDrawBeforePose` → `Queue.FinishPreviousDraw` — the main thread waiting for the render thread to
finish drawing the previous frame before the pose rewrites what that draw reads. The render thread's own time is
unchanged (`queue render thread` 1.819-1.843 ms in A, 1.805-1.839 in B). So that stretch of the frame is bound by the
render thread's draw, not by the main thread's passes, and the frame moved 0.12 ms in the steady window (with 14-A voided)
and not at all over the whole 20 s window. **The next fps target is that draw, or what can run before the wait** — not
another main-thread pass in front of it.

## Predictions, registered before the first run

- **Identity.** Paused `--shot` pairs at the same tick and camera: 0 world pixels differ (world-only mask,
  `2026-10-27-empty-view-breakdown/pixdiff.ps1 -WorldOnly`), both in the empty view and first person. Falsified by any
  world pixel.
- **Empty view** (still camera): the cull runs only when the view changes, so `camera setcamera batches` is ~0 in both
  builds and the steady frame does not move beyond the runs' own spread. `posed 0` in every moment line, else void.
  `batches/marks` (B only) 0 in the steady window. Nothing claimed.
- **First person** (the moving view, crossing clusters): `camera setcamera batches` falls by **0 to 0.15 ms** (finding 83
  put the whole of `batches` at about 0.15 ms). Falsified if B's `batches` sits inside A's spread, or rises.
- **The miss path** (`batches/marks`, B only, carried from `MarkLeaves`): a per-frame mean under 0.05 ms in the steady
  window, since a re-mark (one pass over the leaves) happens only on a cluster change. Falsified above 0.05 ms, which
  would mean re-marking is frequent or slow enough to cancel the walk's saving.
- **Frame:** no claim beyond `batches`; a change under the spread of four runs per build is reported as none.

## Method

Two Release builds out of the tree: A = `752d6981` (origin/main, the merge base's code), B = `87d075e7` (this branch,
which adds the `batches/marks` timer), each from a `git archive` at `D:/measure-f12/pvs-{before,after}-src`, copied to
`D:/measure-f12/pvs-{before,after}`. Every run behind the quiet gate (`run.ps1`: no other viewer, `dotnet`, `testhost`
or `MSBuild` process, idle `/nodemode:1` reuse nodes and `VBCSCompiler` excluded; then three `LoadPercentage` samples
under 25%), under `run-exclusive.ps1`, at 1280x720:

- empty: `TF2VIEW_CAMERA="1613 -2821 154 89 0" <exe> D:/measure-f12/f12.dem --tick 30000 --measure 20 +fps_max 0`
- first person: `<exe> D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0`

Order (`all.ps1`): shots, then ABBA BAAB empty, then ABBA BAAB first person, then a second shot pair. Reading
(`2026-10-27-empty-view-breakdown/read.ps1`): per-second lines after the first five (steady: seconds 6-20 of the
window) frame-weighted; the first five apart (warm). % = part / the steady window's own frame mean. *Corrected
2026-10-28:* this line first called the steady window "the hit path"; that is true of the still empty view only — in
first person it holds cluster changes (below).

## Control

- Ticks advanced ~1,333 per 20 s window.
- Empty view: `posed 0` and a shot with no model, else void.
- `camera setcamera` parts sum to the parent within 0.1 ms (`batches/marks` is nested in `batches`, not added).
- A run that fails or exits non-zero is void and re-run.

## Environment

RTX 3060, i5-10600K, 2026-10-28 01:40-04:04. Every run passed the quiet gate before it started and exited 0. Load
(3 × 1 s, %) before / after:

| run | before | after | | run | before | after |
|---|---|---|---|---|---|---|
| 01-empty-A | 3 7 4 | 17 56 65 | | 09-fp-A | 2 4 1 | 0 4 12 |
| 02-empty-B | 5 1 14 | 29 18 19 | | 10-fp-B | 4 7 2 | 3 3 4 |
| 03-empty-B | 23 19 20 | 16 9 51 | | 11-fp-B | 2 3 6 | 3 100 13 |
| 04-empty-A | 4 6 7 | 24 46 73 | | 12-fp-A | 5 1 5 | 5 4 3 |
| 05-empty-B | 3 4 6 | 28 23 25 | | 13-fp-B | 2 4 5 | 14 4 6 |
| 06-empty-A | 9 3 3 | 7 10 4 | | 14-fp-A | 4 4 4 | 8 7 5 |
| 07-empty-A | 8 1 3 | 4 6 4 | | 15-fp-A | 6 4 4 | 2 3 4 |
| 08-empty-B | 2 3 5 | 7 6 4 | | 16-fp-B | 3 6 5 | 5 7 1 |

The high after-reads are another agent's `-Shared` work (a corpus test run) starting as a run ended; each such run is
judged against its build's other runs below. Raw output beside this file: `2026-10-28-pvs-node-prune/NN-*.txt`.

## Results

**Identity: held, for two cameras at one paused tick.** `pixdiff.ps1` (`2026-10-27-empty-view-breakdown/`) on paused
`--shot`s at tick 30000, whole frame first — the evidence — and with the world-only mask second (the mask also cuts the
y < 50 strip and the chat box, more than the HUD needs). Its stdout per pair: `2026-10-28-pvs-node-prune/pixdiff.txt`.

| pair | whole frame | world only |
|---|---|---:|
| empty A / B | 52 pixels, box (1101,2)-(1119,10) | 0 |
| empty A / B, second pair | 19, box (1113,2)-(1119,9) | 0 |
| empty A / A (control) | 16, box (1113,5)-(1119,10) | 0 |
| first person A / B | 36, box (1113,2)-(1119,10) | 0 |
| first person A / B, second pair | 13, box (1113,2)-(1119,7) | 0 |
| first person A / A (control) | 13, box (1113,5)-(1119,10) | 0 |

Every whole-frame difference lies inside x 1101-1119, y 2-10 — a top-bar HUD element that differs between two runs of
one build as much as between builds. **Scope:** two cameras (the empty view and gummo's first-person eye) at one tick;
no B/B control pair was taken; nothing on a moving path.

**Controls.** Every first-person run but one ended at tick 31347-31348 with 18 rebuild reports and drew 2,577-2,658
frames. **14-fp-A is VOID**: it ended at 31337 with 16 rebuild reports and 2,288 frames, its warm window holds an 18.9
ms frame mean, and its steady frame (7.100 ms) is 0.30 ms above the other three A runs — the stall is not confined to
the warm window. Its figures are listed (†) and left out of every A range below. Empty view: `posed 0` in every moment
line of all eight runs (of 1,851-1,888 selected).

**First person, steady window (seconds 6-20), ms and % of that run's steady frame:**

| run | frame | `setcamera` | `batches` | `sky` | `overlays` | `camera wait` | `queue render thread` | `batches/marks` |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 09-A | 6.781 | 0.770 (11.4%) | 0.420 (6.2%) | 0.198 (2.9%) | 0.143 | 0.263 (3.9%) | 1.843 | — |
| 12-A | 6.796 | 0.769 (11.3%) | 0.420 (6.2%) | 0.198 (2.9%) | 0.144 | 0.253 (3.7%) | 1.834 | — |
| 15-A | 6.753 | 0.761 (11.3%) | 0.415 (6.1%) | 0.198 (2.9%) | 0.140 | 0.263 (3.9%) | 1.819 | — |
| 14-A † | 7.100 | 0.781 (11.0%) | 0.426 (6.0%) | 0.204 (2.9%) | 0.145 | 0.268 (3.8%) | 1.851 | — |
| 10-B | 6.636 | 0.406 (6.1%) | 0.212 (3.2%) | 0.052 (0.8%) | 0.138 | 0.562 (8.5%) | 1.818 | 0.000 |
| 11-B | 6.694 | 0.415 (6.2%) | 0.214 (3.2%) | 0.052 (0.8%) | 0.140 | 0.568 (8.5%) | 1.839 | 0.000 |
| 13-B | 6.582 | 0.404 (6.1%) | 0.208 (3.2%) | 0.050 (0.8%) | 0.138 | 0.554 (8.4%) | 1.805 | 0.000 |
| 16-B | 6.704 | 0.413 (6.2%) | 0.214 (3.2%) | 0.051 (0.8%) | 0.139 | 0.566 (8.4%) | 1.838 | 0.000 |

† 14-A, void (above).

- **`batches` 0.415-0.420 → 0.208-0.214 ms: −0.21 ms**, 6.1-6.2% → 3.2% of the frame. The prediction (0 to 0.15) is
  **falsified on the high side**, and why the baseline was low is **unexplained**: it came from finding 83's last window
  (`batches` 0.15, a Debug build of 2026-10-20); finding 86's table has `batches 0.22, sky 0.15`. A's `batches` here is
  0.42, about twice finding 86's. Builds, windows and main's code all differ between the three, and none was cross-run.
- **`sky` 0.198 → 0.050-0.052 ms: −0.15 ms**, 2.9% → 0.8% — not predicted. The sky view runs its own `WorldVisibility`
  from the sky camera when the view moves (`WorldCulling.SkyRunsFrom`), which used to descend every frustum-kept node of
  the whole map to reach the sky room's leaves.
- **`camera setcamera` 0.761-0.770 → 0.404-0.415 ms: −0.36 ms, 5.3% of A's frame.** `overlays` is unchanged
  (0.138-0.144): it reads the walk's output, which is identical.
- **Where the saving went: into `camera wait`, +0.30 ms** (0.253-0.263 → 0.554-0.568; `queue wait FinishDrawBeforePose`
  reads the same). `Device3D.FinishDrawBeforePose` is `Queue.FinishPreviousDraw`: the pose may not start until the render
  thread has finished drawing the previous frame, because it rewrites what that draw reads (`ViewerPresenter.PoseOver`,
  finding 98 §9 step 5). The render thread's time did not change (1.819-1.843 → 1.805-1.839 ms). So the main thread
  finished `setcamera` earlier and waited longer for the same draw. *Interpolated:* which main-thread work overlaps that
  draw before the wait (the order of the frame's phases suggests the next frame's `sound`, `camera view` and `setcamera`);
  not timed apart here.
- **`camera` total:** 5.419-5.437 → 5.269-5.372 ms.
- **Frame, steady window:** A 6.753-6.796 ms (mean 6.777), B 6.582-6.704 (mean 6.654): **−0.12 ms, ranges apart** —
  with 14-A voided. Under the rule registered above ("a change under the spread of four runs per build is reported as
  none"), keeping 14-A makes A's spread 6.753-7.100 = 0.347 ms, larger than the difference, so the verdict would be NONE;
  **the steady-window verdict depends on voiding 14-A.**
- **Frame, whole 20 s window** (each file's `measured … mean`): A 7.53, 7.61, 7.76 ms (14-A 8.74), B 7.53, 7.64, 7.53,
  7.66 — **overlapping: no difference over the whole window.** 09-A and 10-B drew exactly 2,657 frames each. The warm
  seconds overlap and lean the other way (B 10.62-11.16 ms, A 9.65-11.55), unexplained.

**The miss path (`batches/marks`) — counted and timed apart, untimed, with the probe `cluster-path`**
(`dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- cluster-path D:/measure-f12/f12.dem cp_process_f12 3 30000 31347`,
output in `2026-10-28-pvs-node-prune/cluster-path.txt`):

- gummo's recorded eye (origin + view offset, a tick at a time) changes cluster **45 times in ticks 30000-31347** (about
  2.3 a second, spread across the whole window: ticks 30005-31198), through 25 clusters, never in none. So the steady
  window does hold misses; the first-person run measured both paths.
- **One re-mark** on cp_process_f12 (5,654 leaves, 2,238 clusters), read from the production `MarkTicks`, alternating two
  clusters the path visits, Release, 2,000 timed after 200 warm: **median 25.6 µs** (p10 25.1, p90 104.5, mean 37.0);
  the control (a repeat from one eye) reads 0.
- **Bound: 45 × 25.6 µs = 1.2 ms over the 20 s window** (4.7 ms at the p90), i.e. about 0.0005 ms a frame at ~2,600
  frames — which is why every steady second prints 0.00. *What the probe does not see:* the viewer's camera is
  interpolated between ticks, so a crossing and recrossing within one tick is not counted.
- The warm seconds print 0.01 and 0.04-0.05 ms a frame — 0.04-0.05 × ~55-64 frames is 2.2-3.2 ms in one second, more
  than the 45 changes cost in total. **Unexplained:** the first marks after the seek and their first-use (JIT) cost are
  the likely cause; not timed apart.
- **Not timed:** the sky view's re-marks. `batches/marks` carries only the main view's `MarkTicks`
  (`WorldCulling.MarkTicks => _visibility.MarkTicks`); the sky view's `WorldVisibility` re-marks when the sky eye changes
  cluster, and that time is inside `sky`.
- **Next time, register a per-re-mark bound** (this run registered only a per-frame one).

**Empty view (still camera), steady (here the hit path: the camera does not move):** A 2.658-3.315 ms, B 2.640-3.429 ms (the high ones are 01-A, the session's first run, and
03-B, which started at 19-23% load; without them A 2.658-2.699, B 2.640-2.672); `camera setcamera batches` 0.000 in all eight, `batches/marks` 0.000
in all four B runs, as predicted: a still camera does not re-cull. No difference claimed. Moment (all eight, plain means):
sample 0.30-0.33, drawlist 0.14-0.17, collate 0.20-0.22, unchanged — this change does not touch the entity walks that
remain B259's open floor.

**Against the earlier f12 first-person baselines: the gap is unexplained.** `2026-10-27-static-prop-handle-at-load.md`
measured 8.35-8.39 ms in the same form (Release, 1280x720, 20 s, f12 tick 30000, gummo first person); this record's A
reads 7.53-7.76 ms over the whole window and 6.75-6.80 in the steady one. What differs, stated not weighed: that run
passed `+r_threaded_particles 1 +cl_threaded_bone_setup 0`; it was read by a different script
(`2026-10-27-static-prop-lighting/read.ps1`); and main has merged B607 and B608 since. No build was cross-run to tie the
two baselines (that would be a timed run, not taken). The brief's 8.05-8.06 ms is not tied to this run either.
