# Measurement 2026-10-22: what the viewer's two busy helper threads are, and what they cost in steady state

**Status: written and committed before the run.** Sections above `## Result` are fixed from here; deviations go under Result.

## Observation

`2026-10-22-threads-f12.md` read the viewer on f12 first person (gummo, tick 30000, 1280x720) over the 20 s `--measure` window:
main thread 13.07-15.45 ms a frame, and beside it a managed thread starting at `coreclr.dll+0xd22f0` at 30.7-36.1% of a core
(4.9-5.5 ms a frame) and a runtime thread starting at `coreclr.dll+0x17abf0` at 15.3-16.3% (2.1-2.9 ms a frame). Neither was
named: every managed thread starts at the same `coreclr` trampoline, and the sampler cached each thread's description on first
sight. Its own run A showed the `0x17abf0` thread near idle (1.5%) over the last 11 s, so the window may have held JIT warm-up.

## Question

1. What is each of the two threads: the corpse worker (`ViewerPresenter`'s `CorpseRecording`, a `Task.Run`), another pool worker
   (sound decode via `QueueUserWorkItem`, a `WindowCache` build, a `WindowCheck` walk), the `--measure` heartbeat, the runtime's
   background GC thread, its tiered-compilation worker, the finalizer, or the EventPipe dispatch thread that `--measure`'s own
   in-process `AllocationTickListener` (GC keyword at Verbose) causes?
2. What does each cost per frame in STEADY state: a window starting 10 s after `--measure` begins counting, so asset loading and
   the first seconds of JIT are outside it?
3. GC over the window: which GC mode the process runs, collections by generation, total pause, and whether the main thread stalls
   on a collection (the heartbeat's gap count says whether the whole process stopped).

## Prediction (stated so it can fail)

- The `0xd22f0` thread is the corpse worker (a `.NET TP Worker` running `CorpseRecording`), or the EventPipe dispatch thread.
- The `0x17abf0` thread is the tiered-compilation worker and is under 3% of a core in the steady window.
- The process runs workstation concurrent GC, and gen2 is 0 in the window, so the background GC thread is idle.

## Method

**Build:** `MSBUILDDISABLENODEREUSE=1 dotnet build managed/Tf2DemoSalvage.Viewer3D` (Debug, as the owner runs it), at `origin/main`
`cff99ab0`. GC mode from `tf2demoview.runtimeconfig.json` (no `System.GC.*` keys means workstation, concurrent on) and from the
viewer process itself is not read; the config is the reading.

**Instrument 1, names and CPU** (`2026-10-22-viewer-helper-threads/sample-threads.ps1`): the previous record's sampler changed in
one way: each thread's description (`GetThreadDescription`) is read at EVERY sample, not cached at first sight. .NET sets it from
`Thread.Name` and names its own threads (`.NET TP Worker`, `.NET BGC`, `.NET Tiered Compilation Worker`, `.NET Finalizer`, ...);
whether it does on this runtime is what the control checks.

**Instrument 2, stacks** (one extra run): `dotnet-trace collect -p <pid> --providers Microsoft-DotNETCore-SampleProfiler` for 15 s
inside the steady window, converted to speedscope; each busy OS thread id is matched to its top managed frames. This adds an
EventPipe session to the process, so its CPU numbers are not used; it only names what a thread runs.

**Steady window:** the wrapper (`run.ps1`) launches the viewer under the sampler and watches for the thread named
`measure heartbeat`, which `--measure` creates the frame counting starts. Window = heartbeat + 10 s to heartbeat + 30 s, with
`--measure 40`, so it ends 10 s before the report. Ms per frame = thread CPU over the window / (window seconds x fps), fps from the
`--measure` report's mean (a 40 s mean against a 20 s window: stated as an approximation).

**Runs**, all through `run-exclusive.ps1` (the machine lock; the viewer takes the desktop):
`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 tf2demoview.exe D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person
--measure 40 +fps_max 0`. Run A and run B with instrument 1 only; run C with instrument 1 and the trace. Machine load
(`Get-CimInstance Win32_Processor`.LoadPercentage) before and after each.

**GC:** the `--measure` report's `gc:` line (from `GC.GetTotalPauseDuration` and `GC.CollectionCount`, read on the render thread
at both ends of the 40 s), and its heartbeat gap count.

## Control

- **Instrument 1:** a pwsh process runs a managed `Thread` named `control spinner` spinning, and a thread-pool work item spinning.
  The sampler must read the named thread by its name at ~100% of a core, and the pool thread as `.NET TP Worker` (or some
  runtime-given name) at ~100%. If names read blank, instrument 1 cannot name threads and the stacks decide alone.
- **Instrument 2:** the same control process traced; the spinner's tid must show the spin loop's frames.
- **Repeat:** runs A and B within 15% on mean fps, else the spread is reported beside every per-frame number.
- No TF2 is launched.

## Environment

i5-10600K (6 cores, 12 logical), RTX 3060, desktop 1920x1080, Windows 11. Viewer Debug at `cff99ab0`.

## Result
