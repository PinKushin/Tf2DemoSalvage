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

(pending)
