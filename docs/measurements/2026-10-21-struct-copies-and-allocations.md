# Struct copies and the allocations left on f12 (finding 89)

## Observation

Finding 88's CPU sample of the render thread (f12, `demostf-cp_process_f12-2026-08-08-2207`, tick 30000, first person)
put `Buffer.BulkMoveWithWriteBarrier` at 18.3% inclusive and `PollGCWorker` at 38% as a leaf; its allocation trace left
2.2 MB a frame on the render thread and 5.1 MB on every heap, and the collector paused about 1.1 ms a frame (B567, B573).

**Deviation, stated up front.** The runs below were taken on 2026-10-21 between 15:20 and 18:45, before this file existed:
the measurement gate went live while they ran. This file was written afterwards from their raw output, which is kept
unedited in `2026-10-21-struct-copies-and-allocations/`. Nothing in Method was changed to fit the result.

## Question

1. Which callers make the struct copies under `BulkMoveWithWriteBarrier`, and is that 18% copying or something else?
2. Ranked by bytes, what still allocates per frame on the render thread and on the corpse worker?
3. Do the three fixes made for the largest of them change render-thread bytes a frame, every-heap bytes, the collector's
   pause and the mean frame, and the count of frames over 100 ms in a 60 s run?

## Method

- CPU: `dotnet-trace collect --profile dotnet-sampled-thread-time` over the Debug viewer, `--tick 30000 --first-person
  --measure 25 +fps_max 0`, read by the probe `cpu-trace <trace> 230 252 <render thread>` with leaf filters
  `BulkMoveWithWriteBarrier` and `PollGC` (four frames above each leaf).
- Allocation: the same run with `--profile gc-verbose`, read by `alloc-trace <trace> 4|7 all 200 220 any|<thread>`.
- Timing: `--measure 20` (first person twice per commit, empty view `TF2VIEW_CAMERA="1613 -2821 154 89 0"` once at each
  end), and `--measure 60` three times per side for the stalls. Each commit is built in the same locked script that runs
  it (scratch `series.ps1`/`run.ps1`), and the CPU load is sampled inside the lock before and after each run.
- A run is void when its frame count is clearly below its siblings', or it shows a heartbeat gap, or it overlapped the
  owner's 16:55-17:00 window (a closed process during a Drive install); void runs were rerun.
- Equivalence: `--shot` at first person ticks 30000, 30540, 31000 and a free camera (`-3700 -2300 760 20 200`) at 30540,
  for main twice and the final commit once, compared with `pair-diff`.

## Control

- `cpu-trace`: the render thread is the one whose top leaf is `Device3D.DrawFrame` and whose `EntityModelSet.Instances`
  is ~29% inclusive, as in finding 88. A `BulkMoveWithWriteBarrier` that is a real copy must show as a leaf.
- `alloc-trace`: the `ScenePlayer[]` from `SpectatorTarget.Observable` and `SunFrom`'s tuple arrays, both named in B567,
  must appear; the render thread's MB per 20 s ÷ frames should be near the `--measure` line's render-thread bytes.
- `pair-diff`: main shot twice is the control; a difference outside the fps text box (y 2-10) in the control would make
  the comparison useless.

## Environment

## Result

## Verdict
