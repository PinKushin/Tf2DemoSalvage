# Measurement 2026-10-22: how much of a bone blend is managed glue (finding 101)

## Observation

On f12 first person the viewer's render thread costs about 12-13.7 ms a frame against TF2's main thread 4.9 ms
(`2026-10-22-threads-f12.md`, finding 100). The instrumented `--measure` split (`2026-10-22-f12-camera-breakdown.md`) has no
part above 0.8 ms, and the sampled traces (findings 92, 96) lose 44.5% of their samples to the sampler's own safe-point
pause, so no share below the stage level is a cost. One hypothesis for a gap spread thinly everywhere is managed glue:
interface dispatch on `IReadOnlyList<T>`, large record-struct copies, allocation. The bone blend
(`StudioPoseBlend.BlendSpans`, `StudioBlendGrid.cs:602`) is a pure function of exactly that shape: it reads
`IReadOnlyList<StudioBone>` (a record struct of a string, two `ReadOnlyMemory`, nine tuples) per bone, twice, to get a rest
pose and one flag.

## Question

On real inputs (the shipped `soldier_animations.mdl` skeleton, two `run_` animations at frame 0, read through the
production readers), Release build, BenchmarkDotNet: what does one blend cost, and how does it divide between (a)
allocation (production `Blend` vs the same body into caller arrays), (b) interface dispatch and struct-copy glue
(caller-array copy vs flat per-model arrays and spans), and (c) the arithmetic the engine's `BlendBones` also does (dense
SoA floats)?

## Method

`benchmarks/Tf2DemoSalvage.Benchmarks/PoseBlendBenchmarks.cs`, four variants (doc comment there).

```
MSBUILDDISABLENODEREUSE=1 dotnet build benchmarks/Tf2DemoSalvage.Benchmarks -c Release
pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 -Shared \
  dotnet run --project benchmarks/Tf2DemoSalvage.Benchmarks -c Release -- --filter '*PoseBlend*'
```

`-Shared`: CPU-only, waits behind any desktop/UI run. Output kept in `2026-10-22-pose-blend-glue/`.

## Control

- **Equality**: every variant's output is compared bitwise to production for every bone in `GlobalSetup`; any
  difference throws and the run reports no numbers.
- **The equality check can fail**: setup blends at a different fraction and requires it to compare unequal.
- **Real input is present**: setup prints the bone count and the posed count of each animation; fewer than two non-empty
  `run_` animations throws. A soldier skeleton is expected to have on the order of 80 bones (finding 96 quotes 78-90).
- **Harness noise**: BDN reports the error and StdDev; a difference smaller than three times the larger error is
  reported as no difference.

## Amendment before R2 (review of R1)

R1 (below) is **void as a measure of production**: its `CopyInto` merged production's two `ExpandInto` loops into one,
so it read the skeleton twice a bone where production reads it three times, and it covered only the ordinary path.
Production has two: ordinary (rest-pose seed, reads `IReadOnlyList<StudioBone>`) and additive (identity seed, never reads
it; every player's aim matrix). R2 makes `CopyInto` loop for loop production's `BlendSpans` and adds
`[Params(false, true)] Additive`; the additive pair is the first two delta `aimmatrix` animations with poses, and every
rung seeds identity for it. Same controls. **Inputs are chosen for shape** (bone count, posed counts), not as a pair any
one tick blends.

## Environment

i5-10600K, RTX 3060; Release, .NET 10; branch `perf/where-main-thread-goes` from `504673db`.

## Result R2 (the one to quote)

Inputs printed by setup: ordinary pair as R1; additive pair `a_PRIMARY_aimmatrix_idle_down_right` (18 posed) and
`a_PRIMARY_aimmatrix_idle_down_center` (20 posed), 86 bones. Equality and its control passed on both.

| variant | ordinary | additive | allocated |
|---|---:|---:|---:|
| Production (`Blend`, allocating) | 3,616 ± 126 ns | 1,519 ± 23 ns | 6,928 B |
| CopyInto (`BlendSpans` loop for loop, caller storage: the `BlendInto` cost) | 2,842 ± 80 ns | 1,121 ± 15 ns | 0 |
| Flat (per-model arrays, spans) | 1,094 ± 9 ns | 1,038 ± 9 ns | 0 |
| Arithmetic (dense SoA) | 1,080 ± 9 ns | 984 ± 21 ns | 0 |

(± is BDN's error.) **Verdict R2.** On production's ordinary path the glue is 1,748 of 2,842 ns, **62% (2.6x)**; on the
additive path, which never reads the skeleton, it is 83 ns, **7%**. The glue is almost all one thing: reading the ~100-byte
`StudioBone` through `IReadOnlyList<StudioBone>` per bone (three times a bone on the ordinary path). Flat equals the SoA
floor within noise on the ordinary path; on the additive path SoA is 54 ns below Flat (just over 3x error). R1 (below)
understated the ordinary glue (45%) because its copy read the skeleton twice a bone instead of three times.

## Result R1 (void as production, kept for the wrong turn)

First attempt threw in setup (`soldier_animations.mdl not found`): the path was the install root, and
`GameArchives.Open` takes the `tf` folder. Fixed and re-run; that refusal is the input control working.

**Inputs (printed by setup):** 86 bones; `a_PRIMARY_aimmatrix_run_neutral` (52 bones posed) and
`a_SECONDARY_aimmatrix_run_neutral` (53 posed), the first two non-delta `run_` animations with poses; 0 fixed-alignment
bones. Equality and its control passed (the run would have reported NA otherwise).

| variant | mean | error | StdDev | ratio | allocated |
|---|---:|---:|---:|---:|---:|
| Production (`Blend`, allocating) | 3.159 µs | 0.023 | 0.026 | 1.00 | 6,928 B |
| CopyInto (the `BlendInto` shape: caller storage, `IReadOnlyList`) | 1.928 µs | 0.032 | 0.037 | 0.61 | 0 |
| Flat (per-model arrays, spans) | 1.072 µs | 0.005 | 0.005 | 0.34 | 0 |
| Arithmetic (dense SoA floats) | 1.053 µs | 0.005 | 0.006 | 0.33 | 0 |

All differences exceed three times the larger error except Flat vs Arithmetic (0.019 µs against 3 x 0.005 = 0.015:
marginal; read as equal).

**Verdict.** On the production shape (`BlendInto`, 1.93 µs) the glue is 0.86 µs, **45%**; removing it (Flat) is a
**1.8x** speedup with bitwise-identical output. Flat equals the dense-SoA floor, so the tuples and record struct as
such cost nothing once the reads are direct; the cost was reading a ~100-byte `StudioBone` through
`IReadOnlyList<StudioBone>` twice per bone and the sparse poses through `IReadOnlyList<StudioBonePose>`. What remains
(1.05 µs for 86 bones, ~12 ns a bone) is the normalised lerp the engine's `BlendBones` also does. Allocation (the
public `Blend`) is a further 1.2 µs a call, but production already avoids it (finding 88).

## Verdict

Reused from the inline verdicts in Result R2 and Result R1 above (no Prediction was stated, so there is nothing to
confirm or falsify). **R2 (the one to quote):** on production's ordinary path the glue is 1,748 of 2,842 ns, **62%
(2.6x)**; on the additive path, which never reads the skeleton, it is 83 ns, **7%**. The glue is almost all one thing:
reading the ~100-byte `StudioBone` through `IReadOnlyList<StudioBone>` per bone (three times a bone on the ordinary path).
Flat equals the SoA floor within noise on the ordinary path; on the additive path SoA is 54 ns below Flat (just over 3x
error). **R1 (void as production):** on the `BlendInto` shape the glue was 0.86 µs, **45%**, understated because its copy
read the skeleton twice a bone instead of three times; Flat equalled the dense-SoA floor, and allocation (the public
`Blend`) is a further 1.2 µs a call that production already avoids (finding 88).
