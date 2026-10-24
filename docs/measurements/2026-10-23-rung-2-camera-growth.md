# Measurement 2026-10-23: where the rung-2 `camera` growth sits (analysis of existing raw output)

**This is analysis of the raw `--measure` output already committed for the rung-2 ABBA
(`2026-10-23-queued-render-thread-rung-2.md`, runs in `2026-10-23-queued-render-thread-rung-2/`). No new run was made.**

## Observation

The rung-2 record found `camera` rising from 7.78-8.17 ms (R1, rung 1, mode 2) to 8.34-8.57 ms (R2, rung 2, mode 2), a
mean of +0.57 ms in the frame-weighted phases line, while `camera wait` was only 0.03-0.09 ms. It left the cause open.

## Question

Which sub-parts of `camera` account for that growth, and did they grow by more than the run-to-run spread of their own
condition?

## Method

Script: `2026-10-23-queued-render-thread-rung-2/camera-growth.ps1` (run `pwsh camera-growth.ps1 -Drop 10`).

- Quiet runs only, as the record's table names them: M0 = 01 06 07, R1 = 03 09 11, R2 = 05 08 10 (02 and 04 discarded).
- Source: each run's per-second `frame rate ...` lines (18 per run). Each covers the last 50-141 frames and is a plain mean
  of those frames; the stage means (`sound 0.4, camera 13.1, ...`) and every `parts:` entry on that line are taken. The
  `moment cost` lines (a rolling mean over 100 rebuilds, about 8 per run) are parsed as well.
- **The first 10 per-second samples of each run are dropped** (loading/JIT/warm-up; in every run the first three show
  `camera` at 12-17 ms), leaving 8 samples per run. For `moment cost` the first 5 lines are dropped.
- Per run: plain mean of its kept samples (not frame-weighted). Per condition: mean of its three run means; **spread =
  max - min of the three run means**. A sub-part is flagged GREW/FELL when |R2 - R1| exceeds the larger of the R1 and R2
  spreads. Rows with |R2 - R1| under 0.03 ms (except stages) are omitted.
- Sensitivity: the same with `-Drop 0` and `-Drop 3`, results below.

The window-mean `camera` here (about 7.0 ms) is lower than the record's frame-weighted figure (about 8.2) because the plain
mean over late windows weights quiet seconds the same as busy ones. Compare differences, not levels, against the record.

## Result

Drop 10 (8 samples per run), ms:

| part | M0 | R1 | R2 | R2-R1 | spread M0 | spread R1 | spread R2 | flag |
|---|---|---|---|---|---|---|---|---|
| **stage camera** | 6.97 | 6.56 | 6.98 | +0.42 | 0.19 | 0.30 | 0.23 | GREW |
| camera setcamera | 1.12 | 0.77 | 1.19 | +0.42 | 0.02 | 0.04 | 0.04 | GREW |
| camera setcamera batches | 0.40 | 0.28 | 0.44 | +0.16 | 0.01 | 0.02 | 0.02 | GREW |
| camera setcamera detail | 0.39 | 0.25 | 0.40 | +0.15 | 0.01 | 0.01 | 0.01 | GREW |
| camera setcamera sky | 0.21 | 0.14 | 0.21 | +0.08 | 0.00 | 0.01 | 0.01 | GREW |
| camera setcamera overlays | 0.12 | 0.08 | 0.13 | +0.04 | 0.00 | 0.01 | 0.00 | GREW |
| camera setcamera view / upload / water | 0 | 0 | 0 | 0 | | | | |
| camera pose | 3.56 | 3.57 | 3.53 | -0.04 | 0.17 | 0.13 | 0.02 | |
| camera particles/bursts | 0.79 | 0.82 | 0.78 | -0.04 | 0.02 | 0.11 | 0.04 | |
| camera particles/decals | 0.57 | 0.50 | 0.56 | +0.06 | 0.00 | 0.03 | 0.19 | |
| moment pose (rolling, per rebuild) | 3.75 | 3.73 | 3.76 | +0.03 | 0.25 | 0.07 | 0.25 | |
| stage advance | 1.03 | 1.13 | 1.09 | -0.04 | 0.02 | 0.05 | 0.08 | |
| stage hud | 0.80 | 0.82 | 0.80 | -0.02 | 0.00 | 0.04 | 0.00 | |
| stage sound | 0.18 | 0.16 | 0.17 | +0.01 | 0.01 | 0.00 | 0.01 | |
| stage draw (game thread) | 1.79 | 0.54 | 0.00 | -0.54 | 0.03 | 0.02 | 0.00 | FELL |

(`camera particles` as a whole, `camera view`, `camera wait` and the other particle sub-parts moved under 0.03 ms and are not
listed; `camera wait` is 0.00 / - / 0.07 over all 18 samples, `queue wait FinishDrawBeforePose` 0.08.)

Sensitivity of the one thing that matters, R2 - R1 for `camera setcamera`: +0.42 (drop 10), +0.40 (drop 3), +0.37 (drop 0),
always GREW against spreads of 0.04-0.11. `stage camera` R2 - R1: +0.42, +0.40, +0.46; it exceeds its spread only at
drop 10, because R1's `camera` has a spread of 0.30-0.54 (run 03 again). The other sub-parts never flagged at any drop.

Reading the table the other way round, **R2 is not high; R1 is low.** R2 matches M0 almost exactly in `setcamera` (1.19 vs
1.12) and in `camera` (6.98 vs 6.97), where M0 runs no render thread at all. The record's frame-weighted figures say the
same: M0 8.13-8.26, R2 8.34-8.57 (about +0.3), R1 7.78-8.17.

## Verdict

**Observed:** all of the +0.42 ms (window means; +0.57 ms frame-weighted in the record) is in `camera setcamera`, the camera
cull and plan build. It is spread over its batch, detail, sky and overlay sub-parts in proportion to their size (+0.16,
+0.15, +0.08, +0.04), each beyond its repeat spread. Pose, particles, `camera wait`, hud, advance and sound did not move
beyond their spreads. `setcamera upload`, `water` and `view` are 0.00 in all three conditions, so the water plan and the
detail upload replay are not involved (the `detail` sub-part is the detail-sprite cull in `setcamera`, not an upload).

**Inferred, not measured:**

- The growth is the whole `SetCamera` getting uniformly slower, not one sub-part, which points at something that slows all
  of its memory walking at once (cache or memory-bandwidth contention with the render thread drawing at the same time, the
  record's first candidate), rather than at a specific feature.
- But R2 equals M0, and M0 has no render thread. So the contention story does not explain why R2 matches the
  no-thread case; the likelier reading is that **R1's `SetCamera` was unusually cheap** because in rung 1 the game thread
  waited for the previous draw before the cull, so the cull ran on a quiet machine with caches warmed by the
  just-finished wait. Rung 2 gives that up, putting `SetCamera` back to where mode 0 already had it.
- If so, there is no rung-2 regression to fix; rung 1's `camera` figure was an optimistic baseline, and rung 2's real gain
  over rung 1 (about 0.26 ms at the frame level) is the 0.54-0.79 ms of `draw` wait it removed minus the 0.4 ms of
  `SetCamera` it gave back. Separating "contention" from "R1 was flattered" needs a run that varies one thing (for
  example R2 with the render thread given no work, or `SetCamera` timed alone in R1 and R2 on the same frames).

Not established: any cause below the `SetCamera` level; the sub-parts' own internals were not split further in these logs.
