# Frame stalls and main-thread JIT, f12 gummo first person (2026-10-24, B595)

## Observation

Baseline of `origin/main` `f049abfb`, f12 gummo first person, `--measure 20`, Debug, 1280x720: mean frame 9.62 ms, but
4 frames over 100 ms in 20 s — 127 ms (mostly `between`), 114, 132, 114 ms (mostly `camera`), gc paused ~0 ms, no
heartbeat gap — and `jit: 7321 methods compiled, 3390.1 ms compiling`. The hitches are what a user feels and are not in
the means.

## Question

What runs inside those stalled frames, and how much of the JIT cost lands on the main thread inside frames?

## Prediction (written before any run)

Main-thread tier-0 JIT accounts for most of each `camera` stall: a first-time code path (a new particle system, a new
model's first pose) compiles tens of methods on first call. `DOTNET_TieredCompilation=0` makes those stalls LONGER
(full-opt JIT on first call); ReadyToRun makes them shorter. The `between` stall is a collection whose pause was read over
the wrong window.

## Instrument (built before the runs; `PauseLedgerTests`, `FrameCountersTests`)

- `PauseLedger` now counts and sums every frame over **50 ms** and lists each with a detail clause; the 100 ms list
  (`stall at`) is unchanged, and 50-100 ms frames print as `slow at`.
- The detail is `FrameCounters.Since`: running totals read at each frame boundary on the main thread, differenced —
  `JitInfo.GetCompiledMethodCount/GetCompilationTime(currentThread: true)` (main thread) beside the process total (other
  threads), the camera upload's five pieces as `UploadCameraOver` timed them (`ViewerPresenter.CameraSpent`), the
  sound cache's in-frame decodes, CPU model vertex count, and distinct particle systems born.
- **Window fault fixed.** The stall's `gc paused` was read from `RenderFrame` entry, after `between`, while its length ran
  from the previous sequence end. It is now differenced over the same boundary as the length.
- A `main thread jit:` line prints beside the existing process `jit:` line for the whole measurement.

Not instrumented: texture/material first-use (no running counter exists); a stall whose detail names nothing is
in something uncounted.

## Method

- Demo `D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 60 +fps_max 0`,
  `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720`, Debug build of this branch, through `run-exclusive.ps1`.
- Per variant 3 runs; per run: frames over 50 ms, ms in them, frames over 100 ms, main-thread jit.
- Variants: baseline; `DOTNET_TieredPGO=0`; `DOTNET_TC_QuickJitForLoops=0`; `DOTNET_TieredCompilation=0`; ReadyToRun
  (`dotnet publish -r win-x64 -p:PublishReadyToRun=true` into the scratchpad — a command-line property, no csproj change).

## Control

Baseline is the control for every variant. Instrument control: the per-thread JIT counter must read non-zero over the
measurement (the main thread JITs on first calls by construction); a zero would mean the counter is not per-thread.

## Results

Branch `perf/b595-frame-stalls` @ `82c24269`, Debug, 0 warnings. CPU load before each run 2-22%. Runs were interleaved
(run 1 of every variant, then run 2, then run 3). Raw stdout is in the scratchpad, not committed.

| variant | mean ms (r1/r2/r3) | frames >50 ms | ms in them | frames >100 ms | main-thread jit ms | all-thread jit ms |
|---|---|---|---:|---:|---|---|
| base | 9.05 / 9.11 / 9.04 | 2 / 1 / 2 | 113 / 59 / 242 | 0 / 0 / 1 | 257 / 262 / 251 | ~3,500 |
| `TieredPGO=0` | 10.91 / 10.95 / 10.90 | 0 / 5 / 0 | 0 / 504 / 0 | 0 / 3 / 0 | 62 / 65 / 66 | ~980 |
| `TC_QuickJitForLoops=0` | 10.24 / 10.30 / 10.18 | 0 / 7 / 0 | 0 / 651 / 0 | 0 / 3 / 0 | 61 / 60 / 57 | ~1,540 |
| `TieredCompilation=0` | 11.27 / 11.37 / 11.27 | 0 / 4 / 0 | 0 / 329 / 0 | 0 / 2 / 0 | 74 / 79 / 73 | 80-86 |
| ReadyToRun (publish, Debug) | 9.16 / 9.22 / 9.07 | 2 / 12 / 2 | 122 / 1237 / 117 | 0 / 6 / 0 | 20 / 20 / 20 | ~5,350 |

An earlier single base run (same build) had 1 slow frame of 72 ms (pose 67.4 ms) and 260 ms of main-thread jit.

**The variant does not predict the stalls; the time does.** Run 2 holds 29 of the 37 slow frames, across all five
variants. `TieredCompilation=0` cuts all-thread JIT to ~80 ms and still had two frames over 100 ms in run 2. ReadyToRun
cuts main-thread JIT from ~255 ms to 20 ms and had the worst run. **The prediction is refuted: JIT does not make the
stalls.** Every slow frame past the measurement's first 0.25 s shows `main jit 0 methods 0.0 ms`. The main-thread JIT
(~255 ms base) lands almost wholly in the opening frames, and the first frame (~0.19 s) is the only one that carries
any (80 methods, 5.6 ms). Run 2's clustering is unexplained. The quiet gate read 4-11% load before each run, so a
contender that began mid-run is not excluded.

### Where the slow frames went (all 38, every variant, by the carried counters)

| part | frames | range ms | sub-attribution (log `SLOW MOMENT`, same frame) | what is counted as NOT in it |
|---|---:|---|---|---|
| camera → **pose** | 17 | 46-133 | `viewmodel` 42-128 ms (5 lines), or `baked` 19-49 ms (static-prop colour mesh and lighting, 12+ lines); several pose stalls print no `SLOW MOMENT` line at all, so their time is in `PoseOver` outside `MomentScene.Build` | main jit 0, gc ≤3 ms, no sound decode, no new particle system |
| camera → **particles** | 8 | 30-173 | at t ≈ 0.19 s in every run (the first measured frame), and in ReadyToRun at t ≈ 6.8 s with **64-139 ms of JIT on other threads** in the same frame; 1 frame (191 ms) has a 147 ms GC pause and a heartbeat gap | main jit ~0 |
| camera → **wait** (the render thread's frame in flight) | 5 | 57-95 | the main thread blocked on the queue; the render thread's own time is not split per frame | main jit 0, gc 0 |
| **between** (outside the frame sequence) | 6 | 48-133 | nothing counted: gc ≤3 ms, main jit 0, no heartbeat gap — the main thread was busy or blocked outside `FrameSequence` (message pump, idle loop) | everything this instrument counts |
| sound | 1 | 64 | — | no decode counted (`sound decodes 0`) |

**Instrument controls.** The per-thread JIT counter read 20-262 ms over the run, so it is not zero by construction.
The `entity model vertices` counter read 0 in every run, both at start and at end (`entity model vertices on the CPU 0`).
**It is a dead counter**, since GPU skinning holds no CPU vertices, and it cannot name a model first use. No counter
exists for texture/material first use.
