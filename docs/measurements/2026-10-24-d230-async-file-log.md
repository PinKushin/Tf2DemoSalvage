# Does the background log writer remove the log's stalls, and what does a hard kill cost? (2026-10-24, D230, B595)

Follows `2026-10-24-b595-log-writer-stalls.md`, which attributed 6 of 7 frames over 100 ms to the main thread's
per-line flush. D230 moved the flush to a background thread.

## Prediction (written before the runs)

- **f12 grid:** the async arm has 0-1 frames over 100 ms in total over 5 runs, against roughly 7 for main (the
  part-3 normal arm), and its main-thread log clause reads `flush 0.0 ms` with an enqueue total under 1 ms a run. Frames
  over 50 ms fall to about the warnlog arm's level (0-2 a run, the start-up frames).
- **If false:** the async arm keeps 100 ms frames, and their slow-frame lines show no log time, which would mean the
  flush was only where the stall landed, not its cause.
- **Hard kill:** the synchronous writer loses 0 lines; the async one loses the lines queued at the kill, usually 0-2 at
  one line a millisecond, more when the kill lands inside a slow flush.
- **Frozen logging thread, then kill:** 0 lines lost, because the writer thread keeps draining.

## Method

Arms: `arm-main-a5c9` (main at `a5c9f8d5`) and `arm-async-57f4` (branch `perf/b595-async-file-log` at `57f46d59`),
both Debug, 0 warnings, built through `run-exclusive.ps1 -Shared` and copied with `cp -rn` into new scratchpad folders
created for this run. Their `tf2demoview.dll` and `Tf2DemoSalvage.Logging.dll` differ by md5, and only the async
arm's Logging dll contains `QueueReading`.

f12: `D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 60 +fps_max 0`,
`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720`, through `run-exclusive.ps1`, 5 interleaved runs per arm (main
first), each preceded by three CPU samples under 25%.

Hard kill: `tests/Tf2DemoSalvage.Logging.KillHelper` (Release) logs line `n` at `n` ms after a `start <UTC ticks>`
line. A PowerShell driver kills it with `Process.Kill` 400-1500 ms after launch (seeded `Random`), and counts
`lost = (kill time - start) in ms - last number on disk`. 30 kills per arm, interleaved; `sync` is the pre-D230 writer
(held stream, `AutoFlush`), `async` the real `FileLogWriter`. `freeze` stops the logging thread at line 500 and is
killed 1.5-2.5 s later.

## Results

### Hard kill (measured; run BEFORE the prediction above was written, so the hard-kill prediction is not blind)

| writer | kills | lines lost (= ms) |
|---|---|---|
| sync (pre-D230) | 30 | 0 in 29; one reads -6, clock skew between parent and child |
| async | 29 valid | median 1; 24 runs 0-2, three 4-7, two 55-56 (the kill landed inside a slow flush) |
| freeze, then kill | 10 | 0 in all 10: the log was current at the freeze |

Async kill 30 is void: it was killed before it created its folder and the driver reused the previous run's row.
Raw rows: scratchpad `kills-1/kills.csv`. The loss window is the lines queued at the kill: about 1 ms usually, and as
long as one slow flush (about 55 ms here) at worst.

### f12 grid — VOID, not yet run

Stopped after run 1. Another agent's measurement held the machine (`run-exclusive` named PID 26616); the quiet gate
failed both run-1 entries after 30 tries (CPU 39-100%). Nothing from it is used. **Still to run** on a quiet machine,
with these same arm folders.
