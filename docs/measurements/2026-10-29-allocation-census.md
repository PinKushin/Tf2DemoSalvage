# 2026-10-29 — which of B599's open allocation sites still allocate on main, and how much

**Status: written and committed before the run.** Sections above `## Result` are fixed from here; deviations go under
Result.

## Observation

`docs/RISKS.md` B599 lists the open allocation sites "largest first" from the 2026-10-25 attribution
(`measurements/2026-10-25-allocation-attribution.md`, build `077d0175`). Since then at least two of them were changed by
other work: `OcclusionQueries.Count`'s `float[]` per query became one reused buffer (`c7a81430`, B597), and
`DrawTally.NotDrawable`'s key strings are kept per prop object (`eb9910a3`, B259). The ranking is therefore stale, and
it is the count-first evidence for every item on this branch (`perf/alloc-sites-2`). Read today, not measured: on the
current code `WeaponModels.AttributesFor` asks `ItemSchema.DefinitionAttributesFor` on every call, which builds a
`List`, a `HashSet<string>` and walks the prefab chain, and it is called per placed prop per frame through the Paint
and Attachments delegates; the 10-25 record names no such site.

## Question

On `2a46490c` (origin/main), f12, gummo first person (G) and the empty view (E): how many bytes a frame does each of
B599's open sites allocate — `EconAttributes.Resolve` and its callers, `ItemSchema.DefinitionAttributesFor`,
`ImpactParticles.WithImpactEffects`, `OcclusionQueries`, `EntityModelSet.AttachmentsOf`, `PropTransform.ToMatrix`,
`MatrixConvention.To*Matrix`, `EffectEntities.BuildEffectEntities`, `DemoTimeline.SetLive`, `DrawTally` — and which
sites not on that list are now among the largest?

## Hypothesis

None ranked. The two sites changed since 10-25 allocate nothing per frame now.

## Prediction

- `OcclusionQueries` and `DrawTally.NotDrawable` each under 1 KB a frame in both views.
- The econ attribute sites (`Resolve`, `DefinitionAttributesFor`, the delegates in `MomentScene.Build`) together 60-150
  KB a frame in G.
- The run's `allocated … MB a frame` within 0.1 MB of the 10-28 record's B build (G 0.93-0.95, E 0.49-0.51).

## Method

- Build: `2a46490c` from `git archive` at `D:/measure-f12/as2-a-src`, `-c Release` (label `a`).
- Runs, one per view, through `docs/measurements/2026-10-29-alloc-sites-2/run.ps1` (a quiet gate by load — no other
  viewer, three samples under 25%; other agents' dotnet processes are counted and logged, not refused — then
  `run-exclusive.ps1`, 1280x720):
  - G: `run.ps1 -Build a -Mode fp -Name census-fp-a -Seconds 120 -Trace D:/measure-f12/alloc2/runs/census-fp-a.nettrace`
  - E: `run.ps1 -Build a -Mode empty -Name census-empty-a -Seconds 120 -Trace D:/measure-f12/alloc2/runs/census-empty-a.nettrace`
  The viewer command is `<exe> D:/measure-f12/f12.dem --tick 30000 [--spectate gummo --first-person] --measure 120 +fps_max 0`.
  `dotnet-trace collect -p <pid> --providers Microsoft-Windows-DotNETRuntime:0x1:5` is attached as soon as the process
  appears and ends when it exits (keyword 0x1 level 5 is GC verbose, `GCAllocationTick` with managed stacks, no sampler).
- Analysis: a throwaway console project at `D:/measure-f12/alloc2/analyser` (not committed), referencing the
  `TraceEvent` and `FastSerialization` DLLs inside the installed `dotnet-trace` 10.0.745401 tool. It keeps only the
  ticks in the trace's last 60 s (all playback: the run plays 120 s after its seek) and sums `AllocationAmount64` by
  thread (root frame), by type, and by type plus the first three `Tf2DemoSalvage` frames of the stack.
- Frames in that window: the frame counts of the run's last 60 per-second `frame rate` lines.
- Bytes a frame per site = the site's ticked bytes / frames in the window.

## Control

- **Sum of ticks against the runtime's own total:** the window's ticked bytes per frame against the same run's
  `allocated … MB a frame`, within ~10%; a larger gap means dropped ticks or a misaligned window (the trace runs a second
  or two past playback while the process exits).
- **Something that must be present:** the in-process `AllocationTickListener` enables the same event during `--measure`,
  so its `EventWrittenEventArgs` must appear in the trace; and a site this record does not touch and the 10-25 record
  names in both views — `EffectEntities.BuildEffectEntities` — must show nonzero bytes. If either is absent the stacks are
  not being resolved, and an absent site means nothing.
- **E against G:** the model and attribute sites shrink in E (no posed props in view), particle and HUD sites stay.

## Environment

RTX 3060, i5-10600K, 2026-10-30 00:08-00:20. Build `D:/measure-f12/as2-a-src` (`2a46490c`, Release; the archive's first
build printed 117 warnings, an incremental rebuild 0, as the 10-28 record found for its trees). Loads before / after:
G 15 11 10 / 10 4 11; E 5 3 13 / 33 11 100 (the 100 is the analyser of G, started while E ran — E is a census, not a
timing; its shares are by allocation, not time). Another agent's corpus test (one core) ran throughout.

## Result

**Deviations.** The traces cost frames: G read `mean 6.96 ms` over 17,244 frames with ~115-125 fps per-second lines at
the end, against ~6.0 ms untraced (`measurements/2026-10-29-econ-attribute-walk.md`'s A runs); the bytes per frame are
not affected by that (the control below holds). The analyser's window is the trace's last 60 s; frames are the run's
last 60 per-second lines.

**Controls.** Ticked bytes per frame against the run's own `allocated … MB a frame`: G 744.2 KiB vs 0.74 MB; E 374.8 KiB
vs 0.40 MB — within 6%. The main thread carries 89.7% (G) and 93.9% (E). `EffectEntities.BuildEffectEntities` is
present in both views (the positive control). The model/attribute sites shrink in E and the particle sites stay
(below), as predicted.

**The open B599 sites on `2a46490c`, KiB a frame (G / E), from the top 600 type-and-stack sites:**

| B599 item | G | E | sites |
|---|---|---|---|
| econ attributes (`EconAttributes.Resolve`, `ItemSchema.DefinitionAttributesFor`, `PaintFor`'s dictionary, from the Paint/Attachments delegates and `PowerupBottle`) | ~89 | under 3 (none in E's top 30) | `HashSet<int>` 13.5 + 13.4, `Dictionary` 15.7 + entries 5.1, `List` 7.8 + 7.0, growth 2.6 + 2.4 + 0.9 + 0.9, `HashSet` entries 3.6 + 3.4 + 1.5, `HashSet<string>` 1.4 + 1.3 and entries 2.2 + 1.8 + 1.6, … |
| `ImpactParticles.WithImpactEffects` | ~81 | ~53 | `DetailSpriteVertex[]` 54.1 / 45.7 (callees inlined: `ImpactDraw`'s `AddRange([…])`), `ParticleBatch[]` 13.9 + 6.9 / 3.0, `<>z__ReadOnlyArray` 4.3 / 3.7, boxed `IList` enumerators 1.1 + 0.9 |
| `OcclusionQueries.Count` | **0** | **0** | absent from all 600 sites: removed by `c7a81430` (B597, one reused buffer) |
| `DrawTally.NotDrawable` | **0** | **0** | absent: removed by `eb9910a3` (B259, keys per prop object) |
| `EffectEntities.BuildEffectEntities` | ~39 | ~38 | boxed `List<DetailSpriteVertex>.Enumerator` 16.5 / 16.4, `Func<int, SceneProp>` 13.1 / 12.6, boxed `SpritePass` array enumerator 6.4 / 6.1, `ParticleBatch[]` 3.0 / 2.9 |
| `EntityModelSet.AttachmentsOf`, `MatrixConvention.To*Matrix` | ~39 | ~36 | `float[]` at `AttachmentsOf` 15.2 + 6.1 + 3.2 + 1.4, `ToModelMatrix` 6.9, `ToBoneMatrix` 5.8 (the one kept per attachment; the rest are intermediates) |
| `PropTransform.ToMatrix` | 0 visible | 0 visible | inlined into `AttachmentsOf`'s figure, if anywhere |
| `WorldRenderer.DrawModel` (render thread) | ~31 | — | boxed `List<WorldBatch>.Enumerator` 16.7, closure 7.7, `ApplyProxies` enumerators 4.3 + 2.6 |
| `WorldDecals.Placed` | 19.9 | 11.2 | `PlacedDecal` |
| `DemoTimeline.SetLive` | 0 | 0 | absent: it allocates nothing (its cost is a lookup, not bytes) |

**Not on B599's list and now among the largest:** `ItemSchema.BestVisualSection` and its `Func<Entry, string>` per
player per frame (17.0 KiB G, 12.9 E, with `BaseBodygroups`/`ActivityReplacements` beside it ~10 more), `SceneProp`
records from `DemoTimeline.BuildProp` (21.8 G) and `WeaponPropModels.Resolve` (8.3 / 8.6), `WorldDynamicLights.BrushesOf`
`LitBrush[]` (13.7 / 13.6), `VguiRenderer.ShadingConstants` `float[]` (9.8 / 9.7), `LevelLighting.Entry`'s closure (15.3 G).

## Verdict

**Confirmed**, both predictions: `OcclusionQueries` and `DrawTally.NotDrawable` allocate nothing measurable now (0 of
600 sites in both views), and the econ attribute sites are ~89 KiB a frame in G, inside the 60-150 band. The third
prediction is **falsified as worded**: G read 0.74 MB a frame, not within 0.1 MB of the 10-28 record's 0.93-0.95 — the
traced run's frame rate is lower (*interpolated, not tested*: work done per tick rather than per frame is then divided
over fewer frames, which should RAISE the figure, so the cause is not established); the untraced A runs of item 1 read
0.97-1.00 MB (`measurements/2026-10-29-econ-attribute-walk.md`), which is the comparable figure. The ranking for this
branch: econ attributes ~89, impact batches ~81, effect entities ~39, attachments ~39 KiB a frame in G.
