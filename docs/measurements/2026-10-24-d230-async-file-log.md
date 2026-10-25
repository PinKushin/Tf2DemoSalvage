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

### f12 grid, first attempt — VOID

Stopped after run 1. Another agent's measurement held the machine (`run-exclusive` named PID 26616); the quiet gate
failed both run-1 entries after 30 tries (CPU 39-100%). Nothing from it is used.

### f12 grid, 2026-10-25 (measured)

The quiet gate passed every run (samples 1-23%). Main first, then async, for each of the 5 runs. The figures are the
viewer's own `--measure` summary (`pauses:` and `log writes:` lines), which went to the driving task's output file, not
to the per-run `.txt`. Both arms log 305-314 lines on the main thread a run.

| run | arm | mean ms | frames > 50 ms / ms | > 100 ms | main-thread log | writer thread flush | queue |
|---|---|---|---|---:|---|---|---|
| 1 | main | 9.00 | 3 / 291 | 1 | flush 253.1 ms, 4 over 5 ms | — | — |
| 1 | async | 9.16 | 0 / 0 | 0 | enqueue 2.1 ms, flush 0.0 | 8.8 ms | 0 dropped, high water 62 |
| 2 | main | 9.02 | 1 / 117 | 1 | flush 40.6 ms | — | — |
| 2 | async | 9.13 | 0 / 0 | 0 | enqueue 2.0 ms, flush 0.0 | 9.0 ms | 0, 68 |
| 3 | main | 9.18 | 1 / 61 | 0 | flush 76.6 ms | — | — |
| 3 | async | 9.21 | 0 / 0 | 0 | enqueue 2.1 ms, flush 0.0 | 8.9 ms | 0, 67 |
| 4 | main | 9.24 | 1 / 52 | 0 | flush 5.6 ms | — | — |
| 4 | async | 9.39 | 4 / 1,420 | 4 | enqueue 1.6 ms, flush 0.0 | **10,516.3 ms**, 7 over 5 ms | 0, 68 |
| 5 | main | 9.13 | 6 / 735 | 5 | flush 691.1 ms, 6 over 5 ms | — | — |
| 5 | async | 8.96 | 0 / 0 | 0 | enqueue 1.9 ms, flush 0.0 | 268.6 ms, 4 over 5 ms | 0, 61 |

**Totals.** Main: 12 frames over 50 ms (1,256 ms), 7 over 100 ms, and 5.6-691.1 ms of flush on the main thread a run.
Async: 4 frames over 50 ms (1,420 ms), all 4 over 100 ms and all in run 4. The main thread had 0.0 ms of flush in every
run and 1.6-2.1 ms of enqueue for about 313 lines (about 6 µs a line). No line was dropped, and the queue never held
more than 68.

**Async run 4 is not the log.** Its four stalls (430, 407, 174, 409 ms) are in `hud` (three) and `pose` (one), and every
one reads `log 2-5 writes, wait 0.0 ms, flush 0.0 ms` for its own window. In that run the writer thread measured 10.5 s
of flush, against 9 ms in runs 1-3. So the disk stalled for seconds during that run, and other file reads on the main
thread waited on it. **Interpolated:** I did not check that the four stalls fall inside the slow flushes. The writer
reports totals, not times. Either way, the log no longer blocks the main thread, even when the disk stalls for
10 seconds. Main run 5 shows what that disk did to the old writer: 691 ms of flush on the main thread, and 5 frames over
100 ms.

**Prediction:** it held for the log's share. The log's cost on the main thread went from 5.6-691.1 ms a run to 0. It
missed on frame count, because run 4's disk stall reached the main thread through other file reads.

### Normal close (measured, the 10 grid runs)

All 10 logs, in both arms, end on `[viewer] the message loop returned; Main is done`, which `Program.cs` writes after
`Application.Run` returns. The form, render thread and device are disposed before that line. Each log carries the same
9 `shutdown:` lines as the main arm, including `[render] shutdown: idle stopped after 0 ms, device released after 241
ms`. The log provider is the first `using` in `Main`, so it is closed last.

### Content against the synchronous writer (measured, run 1 of each arm)

With timestamps, digits and `SLOW FRAME`/`SLOW MOMENT` lines removed: main 4,203 lines, async 4,202. The 177
differences are lines whose content depends on timing, not order. Prop positions sampled at a different moment, and one
`SLOW CAMERA` warning that only one run crossed. **Order was not checked by this instrument**: `Compare-Object
-SyncWindow 50` compares within a window and does not report a reordering inside it. Order is held by the unit tests
`Dispose_TenThousandLines_AllOnDiskInOrder` and `Write_FromFourThreads_KeepsEachThreadsOrderAndEveryLine`. Runs 2-5 were
not compared.

### Forced exception (measured; the control cannot fail, so this does not prove the handler's `Flush` matters)

The helper's `crash` mode logs 500 lines and a burst of 20,000, then throws unhandled. Its `UnhandledException` handler
logs a line, calls `Flush`, and ends with `Environment.FailFast`. `crash-noflush` is the same handler without `Flush`.
**30 of 30 crashes kept every line, in both modes** (three variants: plain, with `FailFast`, with the burst). The runtime
spends long enough between the throw and the handler, printing the stack, that the writer thread drains everything
either way. So the evidence that `Flush` drains is the unit test
`Flush_WithNoBackgroundThread_PutsEveryQueuedLineInTheSink`, not this experiment. What this does show is that a crash
through the runtime's path loses nothing.
