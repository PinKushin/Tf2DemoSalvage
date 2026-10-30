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
- Runs, one per view, through `docs/measurements/2026-10-29-alloc-sites-2/run.ps1` (the 10-28 quiet gate, then
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

(to be filled at the run)

## Result

(pending)

## Verdict

(pending)
