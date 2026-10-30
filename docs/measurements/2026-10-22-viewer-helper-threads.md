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

Raw outputs in `2026-10-22-viewer-helper-threads/`: `viewer-a.*`, `viewer-b.*` (sampler CSV and whole `--measure` report),
`control.csv`. Run C's CSV, report and trace (~hundreds of MB as speedscope) stayed in the agent's scratch, not committed; the
numbers quoted from it are below. `stacks.ps1` reads a speedscope file per thread.

### Controls

- **Instrument 1 failed first, and the control caught it.** The first control run read every name as `?`: the sampler (and the
  previous record's, from which it was copied) opened the thread with `0x1000`, which is `THREAD_RESUME`, not
  `THREAD_QUERY_LIMITED_INFORMATION` (`0x0800`). That is why the previous record's threads were unnamed. Fixed; the re-run read
  `control spinner` at 93.3% and `.NET TP Worker` at 94.2%, all other threads 1.4% together.
- **Instrument 2:** the control trace showed both spinner threads in `CPU_TIME` under `Stopwatch.get_Elapsed` (the spin loop).
  The sample profiler samples every managed thread at wall time, waiting included, so its percentages are shares of wall time,
  not CPU; used for identity only, as planned.
- **Repeat:** A 13.41 ms (74.6 fps), B 14.18 ms (70.5 fps): 5.6% apart, inside 15%. C (traced) 15.75 ms; its trace session is
  extra load, so C's fps is not used.
- **Load:** A 46% before / 20% after; B 30% / 27%; C 22% before, 52% at trace start, 37% after.

### Deviation

Run A showed the busy pool worker stops within ~10 s of the heartbeat, so the planned trace window (heartbeat +10 to +25 s) would
have missed it. `run.ps1` was changed before run C to start the trace at the heartbeat (15 s).

### Steady window (heartbeat +10 to +30 s)

| thread (name read by instrument 1) | A % core / ms a frame | B % core / ms a frame |
|---|---:|---:|
| main (render thread), `tf2demoview.exe+0x13ba0` | 95.8 / 12.85 | 96.9 / 13.74 |
| NVIDIA D3D11 driver worker, `nvwgf2umx.dll+0xeba250` | 13.1 / 1.75 | 15.5 / 2.20 |
| `dsound.dll+0x37900` (DirectSound mixer) | 1.9 / 0.25 | under 1 |
| `.NET Tiered Compilation Worker`, `coreclr.dll+0x17abf0` | 1.5 / 0.20 | 1.2 / 0.17 |
| 38-39 others | 1.6 total | 2.0 total |
| **process** | **113.9** | **115.6** |

The `coreclr.dll+0xd22f0` pool worker is absent from this table: under 1% in both runs.

### The first ~10 s after the heartbeat (where the previous record's 20 s window began)

| thread | A (9.1 s) | B (9.1 s) | C (11.1 s) |
|---|---:|---:|---:|
| `.NET TP Worker`, `coreclr.dll+0xd22f0` | 68.0% | 65.2% | 57.5% |
| `.NET Tiered Compilation Worker` | 32.4% | 33.6% | 30.5% |

**What the pool worker runs** (run C's trace, its tid 45912): 31% of its 15 s of wall samples are under
`ViewerPresenter.StartCorpseRecord` lambda -> `CorpseRecord.Run` -> `EntityModelSet.UpdateClientSideAnimations` /
`AdvanceCorpses` -> `CorpsePhysics.Advance` -> `IvpRagdollWorld.Simulate` / `PropModels.SkinnedModel.ForActivity`; the other
69% is idle in `PortableThreadPool.WorkerThread` waiting for work. It is the **corpse record pass** (`CorpseRecording`, a
`Task.Run`), which finishes a few seconds into playback and leaves the worker parked. In run A it was 200.8 s old at the first
sample with 94 ms of CPU and took 17.45 s of CPU in all.

Other threads the trace named, all small: the `measure heartbeat` thread (`Heartbeat`), the EventPipe dispatch thread for
`--measure`'s in-process `AllocationTickListener` (`EventPipeEventDispatcher`, 93% asleep), `.NET BGC` (2.2% in the 20 s
before the heartbeat, under 1% after).

### GC (the `--measure` report: 40 s from the heartbeat, read on the render thread)

| run | paused ms a frame | gen0 | gen1 | gen2 | allocated MB a frame | render thread bytes a frame | heartbeat gaps > 50 ms |
|---|---:|---:|---:|---:|---:|---:|---:|
| A | 0.28 | 599 | 125 | 0 | 1.47 | 1,298,497 | 0 |
| B | 0.30 | 582 | 111 | 0 | 1.49 | 1,313,015 | 0 |
| C | 0.35 | 538 | 114 | 0 | 1.55 | 1,347,289 | 0 |

GC mode: `tf2demoview.runtimeconfig.json` carries no `System.GC.*` keys, so workstation GC with concurrent (background) gen2
on. With gen2 at 0 the background GC thread has nothing to do. The pause is 2.1% of A's 13.41 ms frame. Pause includes the
first 10 s.

## Verdict

Confirmed on all three predictions. (1) The `0xd22f0` thread is a `.NET TP Worker` running the corpse record pass
(`CorpseRecording`): 31% of its 15 s of wall samples in run C are under `ViewerPresenter.StartCorpseRecord`, the other 69%
idle in `PortableThreadPool.WorkerThread`. (2) The `0x17abf0` thread is the `.NET Tiered Compilation Worker`: 1.5% (A) and
1.2% (B) of a core in the steady window, under the 3% bound, 0.17-0.20 ms a frame. (3) The config carries no `System.GC.*`
keys (workstation, concurrent), and gen2 was 0 in all three 40 s reports (A 599/125/0, B 582/111/0, C 538/114/0), so the
background GC thread had nothing to do. What the observation answers: the 4.9-5.5 and 2.1-2.9 ms a frame in the previous
record were the first ~10 s after the heartbeat (pool worker 57.5-68.0%, tiered worker 30.5-33.6%), not steady state; in the
steady window the pool worker is under 1% and the threads beside the render thread (12.85-13.74 ms) are the NVIDIA D3D11
driver worker at 1.75-2.20 ms and little else. The GC pause is 0.28-0.35 ms a frame with no heartbeat gap over 50 ms.

Verdict written 2026-10-29 after the fact from the Result above; the run itself was not repeated.
