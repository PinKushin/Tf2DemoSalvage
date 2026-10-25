# Are the `hud`/`pose` stalls left after D230 main-thread file reads waiting on a slow disk? (2026-10-25, B595 part 5)

Follows `2026-10-24-d230-async-file-log.md`, whose async run 4 had four frames over 100 ms (430, 407, 174, 409 ms) in
`hud` (3) and `pose` (1), each with 0 ms of log time, beside 10.5 s of flush on the log writer thread.

## Instruments (added on `perf/b595-sync-io-instrument`)

- **Main-thread archive reads.** `GameArchives.Read` times itself and adds the call to a `[ThreadStatic]` total
  (`GameArchives.ThreadReads`: reads, ms, reads over 5 ms). It is the one function every content reader goes through for a
  file that is not in a map's pakfile (VPK entries via `VpkArchive.ReadFile`, which opens the numbered archive per call,
  and loose files). `--measure` differences the main thread's total per slow frame (`archive reads …`) and over the run
  (`main thread archive reads: …`). Not covered: pakfile reads (in memory), and the load-time reads of settings, configs
  and the demo, which are not frame code.
- **Process page faults.** `GetProcessMemoryInfo`'s `PageFaultCount` (every thread, soft and hard together) read at the
  same frame boundary, differenced per slow frame (`page faults N`) and over the run.
- Test: `GameArchivesReadTimingTests` (3 reads counted on this thread, 0 seen on another; changing `Reads + 1` to
  `Reads + 0` reddens it).

## Prediction (written before the runs)

- **Quiet runs (5 × 60 s):** in play, main-thread archive reads are few (the HUD and models are loaded before the
  window), a few dozen reads and under 20 ms a run. Frames over 100 ms: 0-1 a run.
- **Under a background disk load (3 × 60 s):** if the hypothesis holds, frames over 100 ms appear, and each carries
  archive-read time of the same order as the stall (most of it), or a page-fault count in the thousands with no read time
  (a mapped page waiting on the disk).
- **If false:** stalls appear with 0 archive-read ms and a page-fault delta no larger than a normal frame's. Then the
  stall is not this process's disk reads and what is left is named from the phase split.

## Method

f12: `D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 60 +fps_max 0`,
`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720`, Debug build of this branch, through `run-exclusive.ps1`, each run
preceded by a quiet gate (three CPU samples under 25%). Disk load: a copy of a multi-GB file into the scratchpad, on the
same drive as the viewer's log, started just before the run and running throughout.

## Results

(Below, after the runs.)
