# 101. Where the main thread goes: a long tail; managed glue is one layout choice, large where it occurs, not the gap (2026-10-22)

**Status: the benchmark harness (`benchmarks/Tf2DemoSalvage.Benchmarks/PoseBlendBenchmarks.cs`) is NOT on main.** It is a
new project with a BenchmarkDotNet dependency and reads the game folder from a fixed path; it lives on the branch
`perf/where-main-thread-goes` until adding that project and package is decided. The numbers below were transcribed from
its runs into `docs/measurements/2026-10-22-pose-blend-glue.md`.

**The question.** The owner, 2026-10-22: *"we still need more fps... if we are not matching valve now then we need to
start looking at native code"*, and he suspects the architecture. On f12 first person the viewer's one busy thread costs
about 12-13.7 ms a frame; TF2 splits about the same scene into a main thread of ~4.9 ms and a render thread of ~3.6 ms
(`measurements/2026-10-22-threads-f12.md`). Is the gap managed-code overhead spread everywhere, which a data-layout
rewrite (or native code) would remove, or work?

Evidence classes: *instrumented* (a `--measure` timer, carried from the code, B243), *benchmark* (BenchmarkDotNet on real
inputs, output asserted equal), *arithmetic*, *estimate*. No sampled share is used as a cost.

## (1) Where the time goes, without the sampler

**No ETW sample was taken: the session is not elevated** (checked with `WindowsPrincipal.IsInRole('Administrator')`:
False), and kernel profile sampling needs it. The commands for the owner are in (5). The sampled traces of findings 92/96
are not used here, because 44.5% of their samples are the sampler's own safe-point pause.

The stage split that exists IS unbiased, because each stage is a Stopwatch interval the code itself took
(`measurements/2026-10-22-f12-camera-breakdown.md`, R1/R2, *instrumented*, ms a frame):

| stage | ms | inside it (largest parts) |
|---|---:|---|
| camera: pose (the moment rebuild) | 3.9-4.0 | place 0.7-0.8 (flex 0.34-0.45), simulate 0.5, drawlist 0.4-0.5, lighting 0.2-0.5, setup 0.3-0.4, baked 0.3-0.4, sample 0.3, viewmodel 0.3, collate 0.2, anim 0.1-0.2, rest 0.3 |
| camera: particles | 2.4 | bursts 0.9-1.0, build 0.65, decals 0.52 (since cut to 0.19) |
| camera: setcamera | 0.7 | detail 0.27, batches 0.18, sky 0.13, overlays 0.12 |
| draw | 2.1-2.25 | |
| advance | 1.1 | |
| hud | 1.0 | |
| sound | 0.2 | |
| **sum** | **~11.6** | against a frame-weighted mean of 11.9-12.0 |

**Reconciling the two frame numbers** (*arithmetic*): 11.9-12.0 ms is the camera-breakdown runs' steady mean; the 12.85-13.74
of finding 100 is a different run, a 10-30 s window with a thread sampler attached. Neither is quoted as "the" number;
the gap to TF2 is 6-8 ms either way (measurements/2026-10-22-threads-f12.md, finding 100).

**No instrumented part is above 1 ms** (measurements/2026-10-22-f12-camera-breakdown.md). The frame is a long tail of 0.1-0.8 ms parts. That alone says a single hot
routine is not where the gap is (findings 93, 96, 97 agree), and it is exactly the shape that per-call managed overhead
spread across all the parts would produce. So the test is on one part.

## (2) The experiment: one routine, real inputs, four rungs (*benchmark*)

`measurements/2026-10-22-pose-blend-glue.md`; harness `benchmarks/Tf2DemoSalvage.Benchmarks/PoseBlendBenchmarks.cs`.
The bone blend (`StudioPoseBlend.BlendSpans`) was chosen because it is the purest example of the suspected shape: it
reads `IReadOnlyList<StudioBone>` (a ~100-byte record struct) per bone to get a rest pose and one flag, and sparse poses
through `IReadOnlyList<StudioBonePose>`. Inputs: the shipped soldier skeleton (86 bones) through the production readers,
and for each of production's two paths a pair of real animations chosen for shape, not as a pair one tick blends:
ordinary (rest-pose seed; two `run_` aim-matrix animations, 52/53 bones posed) and additive (identity seed, the path every
player's aim matrix takes; two delta `aimmatrix` animations, 18/20 posed). R2 of the record; R1 is void (its copy of
production read the skeleton twice a bone, not three times, and understated the glue as 45%).

| rung (ns a call) | ordinary | additive | what it removes |
|---|---:|---:|---|
| public `Blend` (allocates 6.9 KB) | 3,616 | 1,519 | |
| `BlendSpans` loop for loop into caller storage = the `BlendInto` cost | 2,842 | 1,121 | allocation (production already avoids it) |
| flat: per-model arrays, spans | 1,094 | 1,038 | interface dispatch and the bone struct copy |
| dense SoA floats | 1,080 | 984 | (floor) |
| **glue, `BlendInto` minus flat** | **1,748 (62%, 2.6x)** | **83 (7%)** | |

Output is bitwise identical on all 86 bones for every rung on both paths, with a control proving the check can fail.

**What it says.** The glue is almost entirely ONE thing: reading a ~100-byte `StudioBone` through
`IReadOnlyList<StudioBone>` per bone, three times a bone on the ordinary path. The additive path never reads the skeleton
and has 7% glue; interface reads of the small `StudioBonePose` and the tuples cost little. Once the reads are direct the
routine sits at the SoA floor: what is left (~12 ns a bone) is the normalised lerp the engine's `BlendBones` also does.
So the managed overhead here is not "C# everywhere"; it is a specific layout choice (a large struct behind an interface,
re-read per element), and it is large where it occurs and absent where it does not. **Native code on top was not measured**, deliberately: the task's condition for it was a
flat variant still far off, and this one sits at the floor, with ~12 ns a bone left for a normalised lerp. What native
could add is SIMD over that floor, which C# `Vector128` also emits; finding 96's 0.1-0.2 ms stands as an estimate, neither
verified nor falsified.

**Per frame** (*arithmetic*, bound): calls per frame were not counted (no counter run; see (5)). The blend runs inside
`SetupBones`, which the `setup` timer encloses (*read from source*: `EntityModels.Instances.cs:385-387` takes `setupAt`
before `animating.SetupBones(...)` and adds to `SetupTicks` after it; `anim` is inside `setup`, `MomentScene.cs:69`).
`setup` is 0.3-0.4 ms a frame (*instrumented*, measurements/2026-10-22-f12-camera-breakdown.md). All bone setup, of which
the blend is part, therefore fits in 0.4 ms, and the blend's glue is **≤ 0.25 ms a frame** (62% of the whole bucket, a
ceiling: the bucket also holds additive blends at 7%, accumulation and the rest of `SetupBones`).

## (3) What a restructure of the per-frame scene would buy (*estimate*)

Basis: one routine, measured. The glue fraction is 62% where the code re-reads a large struct through an interface per
element, and 7% where it does not. So the estimate needs, per part, which shape it has, and only one part was measured.

- **Pose-class ceiling:** the parts of the pose that plausibly re-read large records per element (setup, baked, collate,
  lighting lookups, place/build other than flex): about 1.2-2.0 ms instrumented (measurements/2026-10-22-f12-camera-breakdown.md). Flex (0.34-0.45) is excluded: its hot
  loop, `FaceFlex.IntoFlexedSlots` (`FaceFlex.cs:224`), already runs over spans of arrays. At 62% as a ceiling:
  **≤ 0.7-1.2 ms**; at the additive path's 7%: about 0.1 ms. The truth is between and was not measured.
- **Particles (2.4 ms): unknown.** Not benchmarked; no fraction is applied to it here.
- drawlist, simulate, sample, setcamera, draw: list building and D3D calls; not this class (findings 96, 97).

**So a flat, persistent, updated-in-place pose buys at most about 1 ms of a 12 ms frame (likely less)**, against a
6-8 ms gap (and ~7-8 ms of game-thread work against TF2's 4.9 even after draw+HUD move to a render thread per D228).
Particles could add to that only once one of their parts is measured to have the same shape. Finding 94's estimate was
seven times too high; if this one is wrong the same way, it is wrong high. **Managed glue is real and
worth removing, but it is not the gap.** The rest of the gap is the long tail itself: the viewer does many small things
per frame that each cost about what the engine's equivalent costs, plus the parts finding 97 named where the engine
does less.

**Native code does not follow from this.** The glue a native routine would avoid is removed at the same cost in C#
(flat = SoA floor, measured); D2's trigger (a specific piece that C# cannot make fast) is not met by the routine tested.

## (4) Recommendation, in order

1. **Flatten the bone path first** (`StudioPoseBlend`, `SkeletonPose` accumulate, `PoseBetween`): per-model rest pose and
   flags extracted once into arrays at model load; poses kept dense in per-entity arrays updated in place, not
   re-expanded from sparse lists per call. Measured 2.6x on the ordinary blend (measurements/2026-10-22-pose-blend-glue.md); buys ≤ 0.25 ms (ceiling, (2)). Cheapest proof the
   pattern carries, with the benchmark already written as its regression harness. The rule it tests is narrow: **never
   read a large record struct through `IReadOnlyList<T>` per element in a per-frame loop**; extract the fields the
   loop needs into arrays once.
2. **Then audit `baked`, `collate`, `lighting` and `place`'s non-flex parts for that one shape** (instrumented 1.2-2.0 ms
   together, measurements/2026-10-22-f12-camera-breakdown.md), and rewrite only where it is present, each behind a
   benchmark of this kind (production vs flat, bitwise equal). Not flex: already spans over arrays.
3. **Benchmark one particle part (bursts, 0.9-1.0 ms instrumented, measurements/2026-10-22-f12-camera-breakdown.md) before touching particles**, to learn its shape; apply no
   fraction to it by assumption.
4. **Keep the render thread (D228)** as the larger single lever (draw+HUD ~3.1-3.3 ms instrumented here).
5. **Do not start native code** on this evidence.

## (5) Not verified, and what needs the owner

- **ETW CPU sampling needs elevation.** For the owner, in an elevated PowerShell, from the main checkout's root
  (`C:\Users\pinku\source\repos\PinKushin\Tf2DemoSalvage`). Profiles `CPU` and `DotNET` exist on this machine
  (`wpr -profiles`), and `DotNET` lists `Microsoft-Windows-DotNETRuntimeRundown` (`wpr -profiledetails DotNET`), so
  managed frames resolve:
  ```powershell
  $env:MSBUILDDISABLENODEREUSE='1'; dotnet build managed/Tf2DemoSalvage.Viewer3D
  wpr -start CPU -start DotNET -filemode
  $env:TF2VIEW_AUTOPLAY='1'; pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe tools/corpus/local/demostf-cp_process_f12-2026-08-08-2207.dem --tick 30000 --first-person --measure 30 +fps_max 0
  wpr -stop C:\Users\pinku\f12-cpu.etl
  ```
  Open in WPA, CPU Usage (Sampled), filter to the render thread, steady window after the first 10 s.
- **Calls per frame of the blend were not counted.** The per-frame figure in (2) is a ceiling from the enclosing
  instrumented `setup` bucket (enclosure read from source).
- Only one routine was benchmarked; 62%/7% are the two paths of that routine, not a measurement of any other part.
- The benchmark is Release; the viewer is Debug with `Optimize=true` (D176), so absolute µs may differ from in-viewer.
