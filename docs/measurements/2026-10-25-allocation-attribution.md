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

Windows 11 Pro 10.0.26300, .NET 10, `dotnet-trace 10.0.745401`, build `D:/measure-f12/b585-pool-before/`. No other
agent timing (the lead's go). Loads before / after: **G** 5/6/17 (a first attempt at 10/39/9 was refused by the gate) /
25/8/12; **E** 23/8/12 (after 17/44/7) / 8/11/16.

## Result

**Deviation from Method:** both runs used `--measure 200`, not 300, so measure start is exit − 200 s. The traced
window is playback seconds ~19-80 of G (trace 23:35:29-23:36:30, exit 23:38:30) and ~19-79 of E. Raw: stdout, the
`.nettrace` files and the analyser's output are in the session scratchpad (`alloc/runs/`), not committed.

**Controls.**

| control | G gummo | E empty | verdict |
|---|---|---|---|
| ticks summed, MiB per playback second (trace) vs the run's `allocated` × frames / 200 s | 116.4 vs 123.0 | 117.8 vs 123.8 | within 6%: no ticks dropped |
| ticks summed per frame in the window (frames from the per-second lines for those seconds: G 6,312, E 14,719) vs the run's `allocated … a frame` | 1.11 vs 1.04 MiB | 0.48 vs 0.48 MiB | within 7% (the window boundary is known to ±2 s) |
| main thread's ticked bytes per window frame vs the run's `render thread … bytes a frame` column | 942,000 vs 880,360 | 483,500 vs 470,653 | within 7%: **the column is the main thread** |
| a tick on the in-process listener's dispatch thread (the positive control) | 0.7%: `Object[]` from `EventPipePayloadDecoder.DecodePayload`, `EventWrittenEventArgs`, `MoreEventInfo` | 0.7%, same types | present: thread attribution works |
| E against G: model types shrink, particle/HUD types stay | `Device3D.File` 0, `DetailSprites.Build` 0, `ImpactParticles` 13.1% | — | as expected |

**By thread (G).** Main (`Program.Main`) 81.2%, ~942 KB a frame; the queued render thread (`QueuedRenderThread.Run`,
named `MatQueue`) 18.1%, ~205 KB a frame; the listener's dispatcher 0.7%; nothing else above 0.04%. E: main 96.0%,
render 3.3%.

**Top 25 types, G** (KB a frame = bytes ÷ 6,312 window frames; m/r = main/render share; the allocating site is the
first non-BCL frame of the heaviest stack):

| % | KB/frame | m/r | type | top site (share of the type) |
|---:|---:|---|---|---|
| 11.1 | 128 | 53/47 | `float[]` | `OcclusionQueries.Count` (render, 36%); `PropTransform.ToMatrix` (14%); `EntityModelSet.AttachmentsOf` (14%); `VguiRenderer.ShadingConstants` (6%) |
| 9.9 | 113 | 100/0 | `<Influences>d__11` | `StudioDecalProjection.Vertex`'s `yield` iterator, under `DecalEffects.StepModelDecals` → `PlaceModelDecal` (93%) |
| 8.6 | 97 | 100/0 | `(int, float, byte, bool)[]` | `List.AddWithResize` in `DetailSprites.Build` (85%) and `EmitGroup` (15%) |
| 7.8 | 88 | 0/100 | `Device3D.<>c__DisplayClass354_0` | `Device3D.File` → `Classify`'s capturing `TranslucencyCache.For` factory (100%) |
| 6.5 | 73 | 90/9 | `string` | `DrawTally.NotDrawable` (57%); `FaceSources.Voices` (13%); `WorldRenderer.Arithmetic` `ToLower` (render, 9%); `ParticleEffects.Gather` (6%) |
| 5.0 | 56 | 100/0 | `DetailSpriteVertex[]` | `ImpactParticles.WithImpactEffects` (100%) |
| 2.7 | 31 | 100/0 | `SceneProp` | `WeaponPropModels.Resolve` (27%), `UpdateClientSideAnimations` (18%), `PlayerProps.Add` (16%) |
| 2.6 | 29 | 100/0 | `PlacedDecal` | `WorldDecals.Placed` (100%) |
| 2.6 | 29 | 100/0 | `HashSet<int>` | `EconAttributes.Resolve` via `WeaponModels.IsFestivized` (46%) and `MomentScene.Build` lambda (45%) |
| 2.5 | 28 | 100/0 | `int[]` | `LocalLights.Split` (27%), `MomentScene.Build` (8%) |
| 2.2 | 25 | 100/0 | `ParticleBatch[]` | `ImpactParticles.WithImpactEffects` (58%), `FrameParticles.BuildOver` (32%) |
| 1.6 | 18 | 1/99 | boxed `List<WorldBatch>.Enumerator` | `WorldRenderer.DrawModel` (100%) |
| 1.5 | 16 | 100/0 | `List<EconAttributeValue>` | `EconAttributes.Resolve` (88%) |
| 1.4 | 16 | 100/0 | `Dictionary<int, EconAttributeValue>` | `MomentScene.Build` lambda `b__1` under `EntityModelSet.Instances` |
| 1.4 | 16 | 100/0 | boxed `List<DetailSpriteVertex>.Enumerator` | `EffectEntities.BuildEffectEntities` |
| 1.3 | 15 | 100/0 | `LevelLighting.<>c__DisplayClass40_0` | `LevelLighting.Entry` |
| 1.2 | 14 | 100/0 | `LitBrush[]` | `WorldDynamicLights.BrushesOf` list growth |
| 1.2 | 14 | 100/0 | `Func<ItemSchema.Entry, string>` | `ItemSchema.BestVisualSection` |
| 1.2 | 13 | 100/0 | `Func<int, SceneProp>` | `EffectEntities.BuildEffectEntities` |
| 1.1 | 12 | 0/100 | `Func<bool>` | `Device3D.File` (the same `Classify` factory) |
| 0.9 | 10 | 100/0 | `Func<int, BspWorldLight>` | `LevelLighting.StyledAndDynamic` |
| 0.9 | 10 | 100/0 | `HashSet<int>.Entry[]` | `EconAttributes.Resolve`, `DemoTimeline.AdvanceSample` |
| 0.8 | 10 | 100/0 | boxed `List<StudioFlexOp>.Enumerator` | `StudioFlexRules.Run` |
| 0.7 | 8 | 0/100 | `WorldRenderer.<>c__DisplayClass250_0` | `WorldRenderer.DrawModel` |
| 0.7 | 8 | 100/0 | `EconAttributeValue[]` | `EconAttributes.Resolve` |

The top 25 types cover 77.2% of G's bytes (E 80.2%). By kind, G: closures and delegates 17.5%, compiler iterators
10.2%, boxed enumerators (a `foreach` over an interface-typed `List`) 10.0%.

**What the render thread allocates (G, ~205 KB a frame):** the `Classify` closure and `Func<bool>` (~100 KB),
`OcclusionQueries.Count`'s `float[]` (~46 KB), `WorldRenderer.DrawModel`'s boxed enumerator and closure (~26 KB),
`VguiRenderer.ShadingConstants` `float[]` (~7 KB), `WorldRenderer.Arithmetic`'s `ToLower` strings.

**Empty view, top sites:** `ImpactParticles.WithImpactEffects` 13.1%, `StudioDecalProjection.Project` (the iterator)
11.0%, `DrawTally.NotDrawable` strings 9.2%, `EffectEntities.BuildEffectEntities` 8.0%, `AttachmentsOf` 5.1%. The model
decal projection runs in the empty view too: it is stepped per frame whatever the camera sees.

**Large-object heap**, from the same runs' `--measure` tally: G 170 MB over 200 s (`Projected[]` 93 MB, from
`StudioDecalProjection.Project`'s per-call array; `WorldVertex[]` 41 MB), E 113 MB (`Projected[]` 93 MB).

**Predictions.** (1) Main 85-92% of the bytes: **falsified**, 81.2%. The prediction divided the B585 runs' column
(~1.07 MB) by their totals; this run's own column, 880,360 of 1.04 MiB, is 81% too. (2) Top 25 types ≥ 80%: **falsified** for G
(77.2%), held for E (80.2%).

**Observed, not explained:** both views allocate about the same per playback second (G 116-123, E 118-124 MiB/s)
while E draws 2.2× the frames. Per-tick work (the moment rebuild) and per-frame work would both have to shift for that
to be coincidence; it is not tested here.

## Verdict

**Predictions falsified, both narrowly; the attribution is complete enough to act on.** The ~1.04 MB a frame is 81%
the main thread and 18% the queued render thread, and the `render thread … bytes a frame` column of `--measure` is the
MAIN thread's, mislabelled since the queue (B581) gave the viewer a real render thread. The largest single sources are
per-element allocations in three hot loops (the decal iterator, the `Classify` closure, the detail sprite lists:
~27% together), fixed under B599 (measurements/2026-10-25-b599-three-allocation-sites.md); the rest is a long tail of
the same three kinds: closures, iterators and boxed enumerators.
