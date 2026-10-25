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

### The builds (the control on the code reading)

- Folders `cfg-debug-a5c9` (written 23:16-23:17), `cfg-release-a5c9` (23:17-23:18) and `cfg-publish-a5c9` (23:19), all
  after the session start; 0 warnings each. Every one of the eleven project assemblies differs by md5 between Debug and
  Release (e.g. `tf2demoview.dll` `ADE001FD` / `9A753AFE`, `Tf2DemoSalvage.Scene.dll` `E841DD71` / `1350A424`): the right
  build was copied. The publish's library assemblies are byte-identical to Release's; only its `tf2demoview.dll` differs
  (RID-specific build).
- **`DebuggableAttribute`: `DebuggingModes = 2` (IgnoreSymbolStoreSequencePoints) in both.** No `DisableOptimizations`, no
  `Default` tracking in either, so the JIT is told the same thing.
- **`runtimeconfig.json` differs by one line**: Release carries `System.Reflection.Metadata.MetadataUpdater.IsSupported:
  false` (hot reload off), Debug omits it. Nothing in the viewer applies metadata updates and no
  `DOTNET_MODIFIABLE_ASSEMBLIES` is set, so this is not expected to touch the frame; the timings below are its control.
- **The publish**: self-contained, `includedFrameworks` 10.0.13; its `System.Private.CoreLib.dll` and `clrjit.dll` are
  file version `10.0.1326.50504`, commit `4099f839`, identical to the shared 10.0.13 that Debug and Release roll forward
  to; its config properties equal Release's. **Same JIT, same knobs, same library assemblies — not measured** (Method).

### The runs

30 s of playback each, ticks ending 31995-32014 (≈2,000 advanced; control met). Gate = three CPU samples before, each
< 25%, taken inside the lock. Phases ms a frame; `rt` is the render thread's draw.

| run | gate % | after % | mean ms | camera | advance | hud | between | rt | > 50 ms (n / ms) | MB/frame |
|---|---|---|---:|---:|---:|---:|---:|---:|---|---:|
| fp D1 | 7 5 7 | 21 18 15 | 9.81 | 7.54 | 0.86 | 0.76 | 0.44 | 2.29 | 0 / 0 | 1.23 |
| fp R1 | 13 18 15 | 15 12 9 | 9.27 | 7.17 | 0.80 | 0.75 | 0.37 | 2.16 | 3 / 159 | 1.18 |
| fp R2 | 14 16 21 | 19 12 7 | 9.79 | 7.53 | 0.85 | 0.81 | 0.40 | 2.31 | 3 / 181 | 1.21 |
| fp D2 | 22 21 18 | 13 22 20 | 9.76 | 7.50 | 0.85 | 0.80 | 0.40 | 2.29 | 3 / 181 | 1.15 |
| fp D3 | 23 23 21 | 10 12 20 | 9.08 | 6.96 | 0.78 | 0.79 | 0.36 | 2.20 | 3 / 315 | 1.16 |
| fp R3 | 17 20 21 | 30 27 32 | 9.72 | 7.51 | 0.84 | 0.76 | 0.41 | 2.30 | 2 / 111 | 1.22 |
| fp R4 | 20 12 14 | 10 5 19 | 9.45 | 7.34 | 0.79 | 0.74 | 0.39 | 2.24 | 8 / 667 | 1.25 |
| fp D4 † | 5 3 5 | **82 79 46** | 15.03 | 11.69 | 1.23 | 1.06 | 0.65 | 3.02 | 12 / 726 | 1.28 |
| fp D5 † | 17 19 12 | 24 26 **100** | 17.82 | 13.96 | 1.27 | 1.14 | 0.89 | 3.28 | 80 / 5,789 | 1.48 |
| fp R5 | 19 15 9 | 19 99 15 | 9.40 | 7.30 | 0.78 | 0.77 | 0.36 | 2.14 | 4 / 287 | 1.12 |
| fp D6 | 19 15 18 | — | 8.67 | 6.71 | 0.76 | 0.69 | 0.34 | 2.09 | 2 / 174 | 1.10 |
| fp R6 | 16 16 15 | — | 8.62 | 6.67 | 0.73 | 0.72 | 0.34 | 2.06 | 2 / 195 | 1.10 |
| fp D7 | 13 12 11 | — | 8.58 | 6.66 | 0.73 | 0.69 | 0.33 | 2.03 | 0 / 0 | 1.10 |
| empty D1 | 7 4 4 | | 3.59 | 1.92 | 0.61 | 0.68 | 0.21 | 0.26 | 0 / 0 | 0.54 |
| empty R1 | 13 13 14 | | 3.55 | 1.92 | 0.60 | 0.66 | 0.21 | 0.25 | 2 / 316 | 0.54 |
| empty R2 | 15 14 15 | | 3.66 | 1.98 | 0.62 | 0.68 | 0.22 | 0.26 | 0 / 0 | 0.56 |
| empty D2 | 15 14 13 | | 3.49 | 1.91 | 0.59 | 0.64 | 0.20 | 0.25 | 0 / 0 | 0.53 |
| empty D3 | 6 5 4 | | 3.64 | 1.96 | 0.62 | 0.67 | 0.23 | 0.27 | 0 / 0 | 0.54 |
| empty R3 | 6 9 9 | | 3.94 | 2.14 | 0.67 | 0.70 | 0.25 | 0.29 | 3 / 194 | 0.57 |
| empty R4 | 18 21 21 | | 3.45 | 1.89 | 0.57 | 0.63 | 0.20 | 0.25 | 1 / 299 | 0.55 |
| empty D4 | 7 5 4 | | 3.45 | 1.89 | 0.58 | 0.63 | 0.20 | 0.25 | 2 / 276 | 0.53 |
| empty D5 | 24 12 13 | | 3.53 | 1.90 | 0.59 | 0.67 | 0.21 | 0.27 | 1 / 58 | 0.55 |
| empty R5 | 10 10 10 | | 3.83 | 2.06 | 0.65 | 0.69 | 0.24 | 0.35 | 0 / 0 | 0.55 |

**† Excluded, and replaced — a POST-HOC exclusion, said plainly.** D4 and D5 passed the gate before starting, and their
after-samples (82/79/46, and 100 in the last of D5's) show load arriving during or at the end of the run from something
outside the lock. Every phase, the render thread included, is inflated by 50-90% together, which is a contended CPU
rather than a code path. **The rule was chosen after seeing the means, not before**, and no after-sample threshold
separates them cleanly: R5 also has one 99% after-sample and its mean (9.40) is normal. So the exclusion rests on the
runs' own shape (every phase up together, D5's 80 slow frames), not on a pre-stated rule. D6, R6, D7 were added in that
order to replace them (R6 to keep the order interleaved). **With D4 and D5 kept**, Debug reads 11.25 ms (8.58-17.82,
n=7) against 9.38 — the reader can weigh that; the five Debug runs without load are 8.58-9.81, inside Release's range.

**Summary** (valid runs; ranges are run means):

| view | Debug | Release | Debug − Release |
|---|---|---|---|
| first person | **9.18 ms** (8.58-9.81, n=5) | **9.38 ms** (8.62-9.79, n=6) | −0.20 ms |
| empty | **3.54 ms** (3.45-3.64, n=5) | **3.69 ms** (3.45-3.94, n=5) | −0.15 ms |
| first-person camera / advance / hud / between / rt | 7.07 / 0.80 / 0.75 / 0.37 / 2.18 | 7.25 / 0.80 / 0.76 / 0.38 / 2.20 | |
| first-person frames > 50 ms | 8 in 5 runs (0-3) | 22 in 6 runs (2-8) | |
| allocation a frame, first person / empty | 1.15 / 0.54 MB | 1.18 / 0.55 MB | |

The two configurations' ranges overlap almost entirely in both views; the sign of the difference favours Debug, inside
the spread. The between-run drift (D6/R6/D7 all 8.6 ms after D1-R5's 9.1-9.8) is larger than the configuration
difference. The > 50 ms count leans to Release but is a handful of frames per run, which B595 attributes to the log
flush landing at random.

**Deviations from the Method:** two first-person Debug runs excluded post hoc and replaced (above), so first person is
Debug n=5 of 7, Release n=6. The empty block's after-samples are in the raw files (all 4-26%). The first launch attempt
started the viewer through `&`, which does not wait on a WinExe; it was stopped (own PID) and its output discarded
before any run counted.

## Verdict

**H0 holds: the Debug build carries none of the gap.** Predictions 1-3 held (first person both 8.8-10.0 ms: Debug's
mean 9.18 and Release's 9.38 are in it; |Δ| 0.20 and 0.15 ms < 0.3; allocation and stalls indistinguishable). The
falsifier did not fire. At this resolution (run-to-run spread ≈ 1.2 ms first person, 0.5 ms empty) the configuration
accounts for **at most ~0.3 ms of the ~4.5 ms** gap to TF2, and the measured sign is Debug faster. That is what the build
inspection already said: D176's `Optimize=true`, identical `DebuggableAttribute`, no `#if DEBUG` code, the same runtime.
**The release zip is the same program again** (identical library assemblies, runtime 10.0.13 and config). The gap to TF2
is the work the frame does, not how it was compiled; the owner's Debug numbers are the shipped numbers.

Raw output: `2026-10-24-debug-vs-release-f12/`.
