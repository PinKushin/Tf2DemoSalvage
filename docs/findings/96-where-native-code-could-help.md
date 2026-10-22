# 96. No single routine earns native code yet, and the arithmetic is a tenth of the frame (2026-10-22)

**The question.** The owner, 2026-10-22: *"keep working, we still need more fps, if we are not matching valve now then we
need to start looking at native code"*. D2 allows native code only when profiling shows that a SPECIFIC piece needs it.
So the question is which render-thread methods carry self time on f12 first person, what kind of work each one does, and
whether any of them is a piece that native code would speed up and C# could not.

**No new run.** A paired TF2-vs-viewer measurement held the desktop, so this finding reads existing records only. The
evidence classes are *measured* (from a named raw file), *read from source*, *arithmetic* and *estimate* (reasoned, not
measured). Every estimate is labelled as one.

## (1) The instrument, and why its shares are upper bounds

- **Source** (*measured*): `measurements/2026-10-22-f12-camera-breakdown/T1-17308.txt`, the `leaf:` list. That is the
  render thread, 8,028 samples over 16 steady seconds, light load (4-22%). The frame on T1's own `--measure` lines was
  12.03 ms. TF2 on the same scene ran 159-239 fps, which is 4.2-6.3 ms (`measurements/2026-10-21-tf2-vs-viewer-f12.md`).
  The build was Debug with `Optimize=true` (D176, `Directory.Build.props:21-26`), so the JIT was not the unoptimized one.
- **44.5% of the leaves are `PollGCWorker`.** That is the sampler's suspension landing at a GC safe point, not the
  collector (finding 92 (3)). So the method rows below hold only 55.5% of the samples between them. How the other 44.5% is
  spread over its callers is unknown. A row's true share is therefore somewhere between its listed share and about 1.8
  times that (1/0.555), if the poll samples follow the same spread. Both bounds are *arithmetic* on a sampled trace, and
  finding 89/B574 showed that a copy site's sampled share is not its cost. **Every share in the table is an upper bound on
  what rewriting that method could save**, and none of them is a measured per-call cost.
- ms are *arithmetic*: share × 12.03 ms, using the listed share. The 1.8× upper bound is given only on the totals.

## (2) The ranked table (self time, render thread, f12 30000 first person)

Classes: **(a)** arithmetic or data-parallel work the engine also does; **(b)** allocation, GC or copy overhead;
**(c)** algorithmic work the engine avoids; **(d)** driver, D3D or OS call overhead, which native code does not help.

| # | method (leaf) | self share (T1-17308) | ms @12.03 | class | C# SIMD/unsafe potential (estimate) | native potential on top (estimate) | what would verify it |
|---|---|---:|---:|---|---|---|---|
| — | `PollGCWorker` | 44.5% | (5.4) | sampler artefact | n/a | none | not a method; spread over its callers (finding 92) |
| 1 | `Buffer.MemmoveInternal` | 3.2% | 0.38 | b | n/a; remove the copies | none | callers of the memmove in the same trace, then an allocation/copy count |
| 2 | `WorldRenderer.SetMaterial` | 2.7% | 0.32 | d (+ c: eight `Nullable` value args per call) | small: pass a struct by `in`, skip redundant binds | none: the time is state the D3D11 runtime is given | count of calls and of redundant binds per frame |
| 3 | `ExposureHistogram.TryRead` | 2.5% | 0.30 | c | **already removed** (B575, finding 95: occlusion queries, frame 0.10-0.19 ms cheaper) | none | done |
| 4 | `DecalEffects.StepModelDecals` | 2.3% | 0.28 | c | **already fixed** (finding 93 (2): window search, `decals` 0.53 → 0.19 ms) | none | done |
| 5 | `WorldRenderer.DrawModel` | 2.3% | 0.28 | d (+ b: a 25-argument call) | small | none | call count × per-call; D3D11 `Draw*` count against TF2's |
| 6 | `LevelLighting.SunFrom` | 2.2% | 0.26 | c | n/a: it is a ray trace into the sky per light-cache build | none | a count of `SunFrom` calls per frame against the cache's two-builds-a-frame budget (finding 93's open item) |
| 7 | `DetailSprites.Build` | 2.1% | 0.25 | **a** | 2-4× on the per-sprite vertex math if the output is a `Span<DetailSpriteVertex>` rather than `IList<>` (*estimate*: the cost is most likely the interface `Add`, not the math) | ≤10-20% beyond that (*estimate*) | micro-benchmark of `Build` on f12's detail props, `IList` vs span output |
| 8 | `HudEventFeed.Advance` | 2.0% | 0.24 | c (suspected) | n/a | none | read whether it walks the whole event list each frame, as `StepModelDecals` did |
| 9 | `VguiRichText.Paint` | 1.8% | 0.22 | c/d | n/a | none | count of glyphs measured per frame; does it re-layout unchanged text |
| 10 | `WorldVisibility.Descend` | 1.3% | 0.16 | **a** | 1.5-2× with a 4-plane `Vector128` box test (*estimate*) | ≤10% (*estimate*) | micro-benchmark on the f12 BSP at the T1 camera |
| 11 | `AudioOutput.Play` | 1.1% | 0.13 | d | n/a | none | it is on the render thread at all: a timer around it |
| 12 | flex: `FaceFlex.IntoFlexedSlots` + `Deltas` | 1.1 + 0.6% | 0.20 | **a** | 2-4× (a scatter-add of three floats per slot; `Vector128` on contiguous arrays, `Unsafe.Add` to drop bounds checks) | ≤10-20% | micro-benchmark on the soldier's 8,105 flexed slots (finding 92 (4)) |
| 13 | bones: `StudioPoseBlend.BlendInto` + `SkeletonPose.Accumulate` + `PoseOf` + `StudioAnimation.ReadBone` + `Chain` + `BoneChain.Build` | 1.0 + 0.4 + 0.5 + 0.5 + 0.2 + 0.1% | 0.33 | **a** (+ b: all take `IReadOnlyList<StudioBonePose>`) | 2-3× for slerp/blend on `Span<StudioBonePose>` with `Vector128` quaternions (*estimate*) | ≤10-20% | micro-benchmark of `BlendInto` over a 90-bone skeleton, list vs span vs SIMD |
| 14 | `WorldRenderer.BindPipeline` | 1.0% | 0.12 | d | none | none | bind count |
| 15 | `Object.MemberwiseClone` | 1.0% | 0.12 | b | n/a; find the record `with` or clone | none | allocation profile |
| 16 | `PixelVisibility.FractionVisible` | 1.0% | 0.12 | c/d | n/a | none | query count per frame |
| 17 | `Buffer.ZeroMemoryInternal` | 0.9% | 0.11 | b | n/a | none | callers |
| 18 | world lists: `VisibleWorld.Surfaces` + `Batches` + `BlendedByLeaf`, `OverlayRenderLists.Order` | 0.8 + 0.8 + 0.4 + 0.8% | 0.34 | a/c (list building through `Func<int,bool>` delegates) | 1.5× from removing the delegate calls (*estimate*) | none beyond C# | delegate-call count; a micro-benchmark |
| 19 | `TextExtensions.DrawText` / `MeasureText` (WinForms GDI) | 0.8 + 0.3% | 0.13 | d | none | none | which control paints each frame (the trackbar's `GetTrackBarValue` 0.4% too) |
| 20 | `EntityModelSet.SettleEach` | 0.8% | 0.10 | unread | — | — | read it |
| 21 | `OcclusionQueries.Count` + `Poll` | 0.7 + 0.6% | 0.16 | d | none | none | B575's own cost; query count |
| 22 | `DetailSpriteRenderer.Upload` | 0.7% | 0.08 | b/d | none | none | bytes mapped per frame |
| 23 | `Monitor.Enter_Slowpath` | 0.6% | 0.07 | b (lock contention) | n/a | none | which lock |
| — | the rest | under 0.6% each | — | mixed | — | — | — |

## (3) What the classes add up to (*arithmetic*, upper bounds)

| class | listed share | ms (listed) | ms (×1.8 bound) |
|---|---:|---:|---:|
| (a) arithmetic the engine also does: rows 7, 10, 12, 13, 18 | 10.6% | 1.28 | 2.3 |
| (b) copy / allocation / lock: rows 1, 15, 17, 22, 23 | 6.4% | 0.77 | 1.4 |
| (c) work the engine avoids: rows 3, 4 (both already fixed), 6, 8, 9, 16 | 11.8% | 1.42 | 2.6 |
| (d) driver / D3D / GDI / audio calls: rows 2, 5, 11, 14, 19, 21 | 9.5% | 1.14 | 2.1 |

The viewer is at 12.0 ms (T1, measurements/2026-10-22-f12-camera-breakdown.md) and TF2 at 4.2-6.3 ms
(measurements/2026-10-21-tf2-vs-viewer-f12.md), a gap of 6-8 ms (*arithmetic*). **If every class (a) routine cost nothing at all,
the frame would lose 1.3-2.3 ms** (*arithmetic*, the table above) and would still be twice TF2's. A realistic C# rewrite of those rows at 2-3× would
save about 0.6-1.5 ms (*estimate*). The native code on top of that saves 10-20% of what remains, so about
0.1-0.2 ms (*estimate*). That is below the frame's noise between runs (finding 93: the whole frame did not resolve a
0.34 ms change).

**Why native adds so little here** (*read from our code* for the signatures, *estimate* for the costs):
- The hot (a) routines take `IReadOnlyList<T>`, `IList<T>` and `Func<int,bool>`. Their cost is most likely interface
  dispatch, bounds checks and delegate calls, not the arithmetic. A native routine needs its input in a contiguous,
  blittable buffer, and putting it there IS the C# rewrite (`CollectionsMarshal.AsSpan`, arrays of structs). Once that
  is done, `System.Runtime.Intrinsics` `Vector128`/`Vector256` emit the same SSE/AVX instructions a C++ compiler would.
- A blittable P/Invoke costs roughly 10-30 ns a call, or a few ns with `[SuppressGCTransition]` (*estimate*, from the
  runtime's documented transition, not measured here). The calls would be per entity (26-29 posed, finding 93) or per
  batch, so the call cost is negligible, but so is anything left for native code to win on.
- The class (d) rows are calls into D3D11, GDI and the audio API. Silk.NET already makes those as function-pointer calls
  through the COM vtable, which is what C++ does. The fix for them is fewer calls (batching, redundant-state skipping),
  and that is the same in either language.

## (4) How the engine does the (a) rows (*read from source*, token counts only, function bodies not read)

- `src/public/bone_setup.cpp`: 36 lines naming `fltx4`, `FourVectors` or `SIMD`. `SlerpBones` (:1373) and `BlendBones`
  (:1531) are the routines that correspond to `StudioPoseBlend.BlendInto`.
- `src/game/client/detailobjectsystem.cpp`: 54 such lines, corresponding to `DetailSprites.Build`.
- Flex: `R_StudioFlexVerts` is in the closed `studiorender` (`0x18001eb90`, read in disassembly for finding 75). Whether
  its loop is SSE was not recorded there.
- World descend: `R_RecursiveWorldNode` is in the closed engine and was not read.

**Needs a Ghidra or source read before any rewrite:** whether `SlerpBones` and `BlendBones` take the SIMD path on PC
(the token count does not say which branch is live), how `R_StudioFlexVerts` vectorises, and how the engine culls the
world tree. Parity of cost means matching how the engine does the work, so these are reads to do first, not optional.

## (5) Recommendation

1. **Native code is not justified under D2's trigger today.** No single routine is above 3.2% of the render thread
   (*measured*, as an upper bound), the largest is a copy, and every class (a) routine together is at most 2.3 ms. Native
   code adds an estimated 0.1-0.2 ms beyond a C# rewrite of the same rows. Do not create anything under `native/`.
2. **The C# rewrites to try first**, each behind a micro-benchmark with a control (the current method as the baseline,
   identical outputs asserted, run while no UI run holds the lock): bones (row 13), flex (row 12), detail sprites
   (row 7), world descend (row 10). Each step goes span-over-array first and then `Vector128`. The span step is probably
   most of the gain (*estimate*).
3. **The larger share is (c) and (d), and native code does not help either one.** `SunFrom` (row 6) and the light
   cache's build count, `HudEventFeed.Advance` (row 8), the rich-text repaint (row 9) and the WinForms paint (row 19) are
   probably work TF2 does not do every frame. The D3D rows (2, 5, 14) want a count of binds and draws against TF2's own.
4. **The 44.5% poll share is the largest unknown.** Until a sampler that does not suspend at safe points splits it
   (ETW kernel sampling via PerfView, or `dotnet-trace` with the `--clrevents` CPU-sample provider on a Release build),
   no share here should be treated as a cost. Revisit the native question only if a micro-benchmark shows a single
   routine above about 1 ms a frame after its C# rewrite.

## (6) Not verified

- No micro-benchmark was written or run. The machine was busy with the desktop measurement, and a CPU benchmark run
  beside it would have disturbed it and measured its noise.
- Every C#/native ratio above is an *estimate*. None is a measurement on this code.
- Row 20 (`SettleEach`) was not read. The class of row 8 is suspected, not read.
- The engine's SIMD use is token counts in two files, not a reading of the live branch.
