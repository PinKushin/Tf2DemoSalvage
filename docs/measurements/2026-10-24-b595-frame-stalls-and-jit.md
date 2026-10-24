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

(below, as measured)
