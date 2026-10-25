# Are the remaining f12 stalls the file log's writer? (2026-10-24, B595 part 3)

Follows `2026-10-24-b595-first-use-builds.md`. Still unattributed there: pose stalls with no `SLOW MOMENT` line, particles
at start, sound with 0 decodes, `between`, camera wait, 50-190 ms each.

## Hypothesis

The main thread is blocked on `FileLogWriter.Write`: a lock shared with every thread that logs Information, and a
flush per line (`AutoFlush`, kept by the owner's ruling, `docs/memory/logs-are-the-debugger.md`) into the Claude
package's virtualised LocalCache folder. B191's family.

## Prediction (written before the runs)

- **If true:** frames > 50 ms per 60 s run fall sharply, to about 0-1, with the file log held at Warning; and in the
  normal arm the slow frames' `log` clause (the writer's own wait + flush on the main thread) carries most of each
  stall's time.
- **If false:** the count is unchanged between arms (inside the 1-5 per run spread seen in part 2), and the slow
  frames' `log` clause reads near 0 ms.

## Instrument

`FileLogWriter` times each `Write` itself: lock wait (before `lock` to inside it) and write-and-flush (the
`WriteLine`, which flushes under `AutoFlush`), and counts writes whose sum is over 5 ms (`LogWriteTiming`). Two
running totals: per writer (every thread) and per thread (`[ThreadStatic]`). The main thread's is a `FrameCounters`
field, so every slow-frame line carries `log N writes, wait, flush, M over 5 ms` for its own window; the `--measure`
summary prints a `log writes:` line with the main thread's and every thread's totals over the window. Unit tests:
`LogWriteTimingTests` (the inverse edit `_timing -= one` reddened exactly
`Write_ThreeLines_CountsThreeOnTheWriterAndTheCallingThread`, 20/21).
Control: the normal arm must show a non-zero write count; the suppressed arm a near-zero one.

## Method

No shipped switch holds the file log above Information (`developer` maps 0/1/2 to Information/Debug/Trace,
`ViewerSettings.Verbosity`). So the suppressed arm is a throwaway build: `Program.cs`'s
`SetLogVerbosity = level => logs.Minimum = level` changed to `LogLevel.Warning`, built, copied out, the edit reversed.
Never committed. Both builds Debug, copied into new uniquely named scratchpad folders, timestamps checked.

`D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 60 +fps_max 0`,
`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720`, via `run-exclusive.ps1`, 5 interleaved runs per arm, each
preceded by three CPU samples under 25%.

## Results

Branch `perf/b595-log-writer-stalls` at `e305dc2c`, Debug, 0 warnings. Arms copied to new scratchpad folders
`arm-normal-e305` and `arm-warnlog-e305` (their `tf2demoview.dll` differ by md5; the bin copy matched the warnlog arm,
built last). The quiet gate passed in every run (samples 6-23%). Run order: normal 1, warnlog 1, normal 2, and so on.

| run | arm | mean ms | frames > 50 ms / ms | frames > 100 ms | main-thread log writes / flush ms / over 5 ms | all threads |
|---|---|---|---|---:|---|---|
| 1 | normal | 9.38 | 6 / 477 | 2 | 312 / 291.9 / 3 | 767 / 296.1 / 3 |
| 1 | warnlog | 9.81 | 1 / 55 | 0 | 31 / 0.8 / 0 | 31 / 0.8 / 0 |
| 2 | normal | 9.44 | 4 / 373 | 2 | 318 / 234.1 / 2 | 773 / 302.0 / 3 |
| 2 | warnlog | 9.39 | 2 / 112 | 0 | 23 / 0.6 / 0 | 23 / 0.6 / 0 |
| 3 | normal | 9.41 | 1 / 104 | 1 | 320 / 100.7 / 1 | 775 / 104.7 / 1 |
| 3 | warnlog | 9.28 | 0 / 0 | 0 | 26 / 0.6 / 0 | 26 / 0.6 / 0 |
| 4 | normal | 9.40 | 4 / 327 | 1 | 321 / 51.0 / 1 | 776 / 206.1 / 2 |
| 4 | warnlog | 9.35 | 2 / 105 | 0 | 28 / 0.6 / 0 | 28 / 0.6 / 0 |
| 5 | normal | 9.30 | 2 / 161 | 1 | 321 / 9.7 / 0 | 776 / 110.6 / 1 |
| 5 | warnlog | 9.40 | 1 / 53 | 0 | 31 / 0.7 / 0 | 31 / 0.7 / 0 |

**Totals: normal 17 frames > 50 ms, 1,442 ms (range 1-6 a run), 7 over 100 ms; warnlog 6 frames, 325 ms (range 0-2),
0 over 100 ms.** The control holds: about 320 main-thread writes a run in the normal arm against 23-31 with the log at
Warning.

**Attributed by the writer itself, frame by frame.** The normal arm's slow frames carrying a slow write:

| run | at | frame | largest phase | that frame's log flush |
|---|---|---|---|---|
| 1 | 45.3 s | 115 ms | camera (pose 110.7) | 108.7 ms, 1 write over 5 ms |
| 1 | 50.8 s | 95 ms | sound | 80.8 ms |
| 1 | 56.3 s | 111 ms | between 95.5 | 95.3 ms |
| 2 | 18.1 s | 136 ms | between 126.8 | 126.3 ms |
| 2 | 43.9 s | 109 ms | camera (pose 103.6) | 99.9 ms |
| 3 | 14.4 s | 104 ms | sound 93.5 | 93.4 ms |
| 4 | 53.2 s | 56 ms | camera | 43.8 ms |

Seven frames, 44-126 ms of flush each, landing in whichever phase wrote the line: pose, sound, `between`. That is B191's
second point again ("it appeared to move between phases… because all of them log"). **Lock wait is 0.0-0.1 ms in every
run**, so the cost is the flush itself (one line a run in the virtualised LocalCache path takes about 100 ms), not
contention with other threads. Other threads' slow flushes (run 4: 155 ms, run 5: 101 ms on the all-threads line)
do not stall the main thread.

**Not the log, and left open:** run 4 at 2.8 s, a 166 ms `camera wait 150.6` beside 167 ms of other-thread JIT; run 5
at 26.7 s, 110 ms `camera wait 99.1` with no log time; and the 50-58 ms frames in the first 2 s of almost every run
(both arms), whose log clause reads 0.

**Verdict: the hypothesis holds for 6 of the 7 frames over 100 ms in the normal arm.** It does not hold for the
render-thread waits or the start-up frames. Frames > 50 ms fell from 1-6 to 0-2 a run, and the ones left are the
start-up frames, which both arms share.

## Options (for the owner; nothing changed)

Flush-per-line was a deliberate diagnostics choice (`docs/memory/logs-are-the-debugger.md`, B191), so this session
does not change it.

1. **An async writer thread.** `Write` enqueues the line and a dedicated thread writes and flushes. The main thread pays an
   enqueue and never waits on the disk. Flush on `Dispose` and from the crash handlers `Program.cs` already registers
   (`ThreadException`, `UnhandledException`, `UnobservedTaskException`). Cost: a hard kill (task manager, a native crash
   that skips the handlers) loses whatever is still queued, usually milliseconds' worth. Ordering across threads is
   kept by a single queue. This keeps every line and is the closest to the owner's 2026-08 ask, *"that should be using
   the async version, non blocking"*.
2. **Flush every N ms** (`AutoFlush = false` plus a timer or a check in `Write`). Simpler, but a write that triggers the
   flush still blocks its caller, so it makes the stall rarer without removing it. The same crash-loss window as 1, and
   bounded by N.
3. **Demote hot Information lines to Debug.** The ruling B191 and B591 already applied, one line at a time. It keeps the
   durability, but ~320 lines a minute are still Information on the main thread, any of which can be the slow one, so
   this cuts how often a stall happens and never stops it.

Recommendation as measured: 1. Only it removes the main thread from the flush, whichever line is slow.
