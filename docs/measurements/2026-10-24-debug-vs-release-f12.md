# How much of the f12 viewer-vs-TF2 gap is the Debug build? (2026-10-24, B596)

## Observation

The viewer's f12 first-person frame reads 9.0-9.6 ms (`2026-10-24-b595-log-writer-stalls.md`, 60 s runs) against TF2's
4.8 ms on the same scene (`2026-10-22-tf2-vs-viewer-f12.md`). Every viewer number in that comparison came from the
**Debug** build, which is what the owner runs (`docs/memory/debug-is-what-the-owner-runs.md`; the 2026-10-22 records
name `bin/Debug`). The question is whether the configuration itself carries any of the ~4.5 ms.

## What differs between Debug and Release, read before measuring

Read from `Directory.Build.props` (the only `Directory.Build.*` in the tree), the viewer's `.csproj`, `dotnet msbuild
-getProperty` for each configuration, and a grep of `managed/` for `#if`, `Conditional(`, `Debug.`, `Debugger.`,
`IsDebug`:

| item | Debug | Release | effect on a frame |
|---|---|---|---|
| `Optimize` | true (D176, every configuration) | true | none: same JIT input |
| `DefineConstants` | `TRACE;DEBUG` | `TRACE;RELEASE` | none found: no `#if DEBUG`/`#if RELEASE` in `managed/` (every `#if` hit is a quoted SDK shader or header comment) |
| `[Conditional("DEBUG")]`, `Debug.Assert` | none | — | none |
| `Debug.WriteLine` | one, `FileLogWriter.Write`, gated on `Debugger.IsAttached` (B595) | compiled out | none with no debugger attached |
| `DebugSymbols` | true | false | pdb emission only (`DebugType` portable in both) |
| `AssemblyConfiguration` | `Debug` | `Release` | read by `ViewerHost.IsDebug`, whose only use is the F12 screenshot key's sidecar (`ViewerSession.Capture.cs`) — not on the measured path |
| `Deterministic`, `CheckForOverflowUnderflow`, analyzers | same | same | none |
| `TieredCompilation`, `TieredPGO`, GC settings | unset in both | unset | to be confirmed by diffing the generated `runtimeconfig.json` (below) |
| per-project `Optimize` | none in `managed/` (only `benchmarks/` sets it, Release-only) | — | none |

**So the prediction from the code is that the two configurations are the same program to the JIT.** The measurement
is the control on that reading.

**The release zip (`build/package.ps1`)** publishes `-c Release -r win-x64 --self-contained true`: no ReadyToRun, no
trimming, no single-file. It differs from a Release build only by carrying its own runtime copy. It is measured only
if that runtime's patch version or its `runtimeconfig.json` differ from the shared runtime a framework-dependent build
rolls forward to (`10.0.13`); otherwise it is the same JIT with the same knobs. `package.ps1` itself is not run (it
deletes its staging folder); its `dotnet publish` line is run into a new scratchpad folder.

## Hypothesis

H0: the configuration carries none of the gap; Debug and Release f12 means differ by less than the run-to-run spread.

## Prediction (numbers fixed before the runs)

- First person: both means in 8.8-10.0 ms; |Debug − Release| < 0.3 ms (≈3%), ranges overlapping.
- Empty view: both means within 0.3 ms of each other, ranges overlapping.
- Allocation per frame and frames > 50 ms: indistinguishable between configurations.
- Falsified if the means differ by more than 5% with non-overlapping ranges in either view.

## Method

Commit `a5c9f8d5` (branch `perf/debug-vs-release-gap`, no code change). Each configuration built with
`dotnet build managed/Tf2DemoSalvage.Viewer3D -c <cfg> -o <scratchpad>/cfg-<cfg>-a5c9`, via `run-exclusive.ps1 -Shared`.
Control on the builds: timestamps after the session start; md5 of every project assembly per folder (expected to differ —
MVID, configuration attribute, debug directory — which proves the right build was copied, not that it behaves
differently); `runtimeconfig.json` diffed; `DebuggableAttribute` read from each `tf2demoview.dll`.

Runs, each inside one script passed to `run-exclusive.ps1` so the quiet gate is sampled while holding the lock: three
2 s CPU samples, all < 25% (else wait and resample), then

```
TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 tf2demoview.exe D:/measure-f12/f12.dem --tick 30000 \
  --spectate gummo --first-person --measure 30 +fps_max 0                      # first person
TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="1613 -2821 154 89 0" tf2demoview.exe \
  D:/measure-f12/f12.dem --tick 30000 --measure 30 +fps_max 0                  # empty view (no --first-person: finding 82)
```

5 runs per configuration per view, order ABBA (D R R D D R R D D R), first-person block then empty block. Read from each
run's `--measure` summary: mean frame ms, phases (camera, advance, hud, between, render-thread wait), frames > 50 ms,
allocation per frame.

## Control

- Ticks advanced ≈ 66.7 × 30 ≈ 2,000 per run.
- The runtime config and `DebuggableAttribute` comparison above; if either differs the H0 reading from code is wrong
  regardless of the timings.
- Own PIDs only; nothing else killed.

## Result

(written after the runs)
