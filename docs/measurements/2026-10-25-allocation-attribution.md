# Measurement 2026-10-25: what allocates ~1.2 MB a frame, by type, call site and thread

**Status: written and committed before the run.** Sections above `## Result` are fixed from here; deviations go under Result.

## Observation

`--measure` on f12 gummo first person, 1280x720, reads `allocated 1.16-1.25 MB a frame`, gc paused 0.21-0.26 ms a frame
(~2.3% of a 9.5-10 ms frame), gen0 872-1057 per 60 s; the empty view 0.48-0.50 MB and 0.07-0.08 ms
(measurements/2026-10-25-b585-matrix-pool.md, runs 01-12). The same line reads `render thread 1,065,356 bytes a
frame`. That column is `GC.GetAllocatedBytesForCurrentThread` read by `GcReading.Now()` from
`ViewerSession.RenderFrame` (`ViewerSession.Frame.cs:336`, `:547`), so by reading the code it is the thread that runs
`FrameSequence`, the WinForms main thread, not the queued draw thread. The B585 matrices were ~22 KB of the total
(*arithmetic*). Nobody has attributed the rest by type.

## Question

Which types, allocated from which methods on which thread, make up the bytes a frame? Which share is the game (main)
thread and which the queued render thread, the corpse worker, or the runtime's own threads?

## Hypothesis

None ranked. The main thread carries ~1.07 of ~1.2 MB (the column above, if it is the main thread).

## Prediction

- The main thread's AllocationTick bytes are 85-92% of the trace's total (1.07 / 1.16-1.25). Falsified if the queued
  render thread carries more than the main thread.
- The top 25 types cover at least 80% of the sampled bytes.

## Method

The instrument is the runtime's `GCAllocationTick` event, which fires once per ~100 KB allocated per heap, carrying
the type of the object that crossed the threshold, the bytes since the last tick, the thread, and (in EventPipe) the
managed stack. Summed over thousands of ticks it is an unbiased sample of bytes by type and by stack (a type is charged
the whole 100 KB when its object crosses, so a type's share is proportional to its bytes in expectation).

- Build: `D:/measure-f12/b585-pool-before/` (Release, `077d0175`; `077d0175..aa5240ec` touches only
  `Directory.Build.props` version and `RELEASE-NOTES.md`, so it is `origin/main`'s code).
- Viewer, one at a time through `run-exclusive.ps1`, quiet gate first (three `Win32_Processor.LoadPercentage`
  samples under 25%), no other `tf2demoview` running:
  - **G** gummo: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 <exe> D:/measure-f12/f12.dem --tick 30000
    --spectate gummo --first-person --measure 300 +fps_max 0`
  - **E** empty: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="1613 -2821 154 89 0" <exe>
    D:/measure-f12/f12.dem --tick 30000 --measure 300 +fps_max 0`
- Trace: `dotnet-trace collect -p <pid> --providers Microsoft-Windows-DotNETRuntime:0x1:5 --duration 00:00:01:00`,
  attached 240 s after launch (the load is ~3 min; `--measure`'s stdout is only flushed at exit, so a fixed delay,
  per measurements/2026-10-24-main-thread-cpu-profile.md fault 1). Keyword 0x1 at level 5 is GC verbose, which carries
  `GCAllocationTick_V4`; no sampler, so no suspension perturbation (fault 3 of that file).
- Analysis: a throwaway console project in the session scratchpad referencing the
  `Microsoft.Diagnostics.Tracing.TraceEvent.dll` that ships inside the installed `dotnet-trace` tool (a file reference,
  no NuGet package added). `TraceLog` resolves stacks through the trace's own rundown. Per tick: thread id, type name,
  `AllocationAmount64`, and the stack. Reported: top 25 types by bytes, with the first frame outside
  `System.*`/runtime and the top three stacks per type; bytes by thread, the thread named by the frames its stacks hold
  (`Program.Main` = main; the queue's own loop = render; others by their root frame).
- Frames in the traced window: the run's per-second `frame rate` lines for the traced seconds (measure start = process
  exit − 300 s; trace start and end from the trace's own timestamps), frame-weighted.

## Control

- **Sum of ticks against the runtime's own total.** Sum of `AllocationAmount64` over the trace divided by the traced
  window's frames must agree with the same run's `allocated … MB a frame` within ~10%. A large shortfall means ticks
  were dropped (EventPipe buffer) or the window is wrong.
- **Something that must be present:** the B585 instance matrices (`float[]`/the pool's type) were measured at ~22 KB a
  frame by arithmetic on `origin/main`'s predecessor; on this build they are pooled, so their absence is expected. The
  positive control is instead the in-process `AllocationTickListener` itself: during `--measure` it enables the same
  event, so its `EventWrittenEventArgs`/payload objects must appear in the trace on the runtime's dispatch thread.
  If no tick at all lands on that thread, thread attribution is broken.
- **Thread split cross-check:** the main thread's ticked bytes per frame against the same run's `render thread … bytes
  a frame` column (which is the main thread by the reading in Observation): within ~10%, or the reading is wrong.
- **E against G:** the empty view must shrink the model/pose types and keep the particle/HUD ones (finding 97 §6, the
  CPU profile's Control).

## Environment

_To be filled at run time: load samples before each run, build, Windows 11 Pro 10.0.26300, .NET 10._

## Result

_Not run yet._

## Verdict

_Not run yet._
