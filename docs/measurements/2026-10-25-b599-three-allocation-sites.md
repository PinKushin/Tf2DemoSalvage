# Measurement 2026-10-25: the three largest per-frame allocation sites removed (B599)

**Status: written and committed before the run.** Sections above `## Result` are fixed from here; deviations go under Result.

## Observation

The allocation trace (measurements/2026-10-25-allocation-attribution.md, run G) put three call sites at the top of
f12 gummo first person's ~1.04 MB a frame, each allocating per element rather than per frame:

- `StudioDecalProjection.Vertex` → `Influences`, a `yield` iterator per vertex: 9.6% of bytes (12.2% in the empty view).
- `Device3D.File` → `Classify`'s capturing factory for `TranslucencyCache.For`: a closure and a `Func<bool>` per
  collated instance on the render thread, 8.8% (the
  `docs/memory/a-capturing-cache-factory-allocates-on-a-hit.md` pattern).
- `DetailSprites.Build`: two fresh lists (grown by `AddWithResize`) and a capturing sort comparison a frame, 8.9%.

Together ~27% of the gummo bytes. Build `C` (this branch) removes all three: an in-place weighted sum, a state
argument with a `static` lambda, and thread-static scratch lists with a comparison made once.

## Question

How much do allocated MB a frame, gc paused ms a frame, gen0 collections and the frame mean move on f12 gummo first
person and the empty view?

## Prediction

- Allocation falls by 0.22-0.32 MB a frame in gummo (27% of 1.04 ± the run spread) and by ~0.06-0.08 MB in the empty
  view (the decal iterator's 12% plus the detail lists, which the empty view builds too). Falsified if gummo falls by
  under 0.15 MB.
- gen0 count per 60 s falls roughly in proportion (~25% in gummo). The gc pause a frame falls by less than in
  proportion, since a gen0 pause scales with survivors, not with garbage. Inconclusive if inside the spread.
- The frame mean: no prediction beyond "not slower"; a 0.05 ms gain would be under the 9.47-10.51 ms gummo spread of
  measurements/2026-10-25-b585-matrix-pool.md.

## Method

Two Release builds, copied out: **A** `D:/measure-f12/b585-pool-before/` (`077d0175`, `origin/main`'s code), **C** this
branch's commit, `dotnet build managed/Tf2DemoSalvage.Viewer3D -c Release` through `run-exclusive.ps1 -Shared`, copied
to `D:/measure-f12/b599-after/`. Interleaved A C C A A C per subject (three each), 60 s each, one viewer at a time
through `run-exclusive.ps1`, quiet gate (three `Win32_Processor.LoadPercentage` samples under 25%, retried) and no other
`tf2demoview` before each:

- gummo: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 <exe> D:/measure-f12/f12.dem --tick 30000 --spectate gummo
  --first-person --measure 60 +fps_max 0`
- empty: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="1613 -2821 154 89 0" <exe>
  D:/measure-f12/f12.dem --tick 30000 --measure 60 +fps_max 0`

Read from each run's `measured …` and `gc:` lines: frames, mean ms, MB a frame, gc ms a frame, gen0, and the main
thread's bytes a frame (the `render thread … bytes a frame` column, which the trace showed is the main thread).

Safety: one C run gummo with `+mat_queue_mode 2` and `TF2VIEW_QUEUE_VERIFY=1`; its log's last `queue verify` line, and
any `inputs changed` line. The same log's `queue verify` lines' timestamps against the run's load and play, to say what
the counter counts.

## Control

- Same-build spread beside each difference (three runs a build).
- Ticks advanced ≈ 66.7 × 60 per run.
- Equivalence of each change is held by tests, each sabotaged: `StudioDecalProjectionTests` (a thousand vertices
  allocate what one does; a three-bone weighted sum), `DetailSpritesTests` (a repeated build allocates 0 and builds
  the same quads), `TranslucencyCacheTests` (a hit allocates 0), and the existing conformance suites for the values.
- The removed sites must be gone from a trace of C: one 60 s `GCAllocationTick` trace of C gummo, same method as the
  attribution file, must show no `<Influences>d__11`, no `Device3D.File` closure, and no `DetailSprites.Build` list
  growth. If any is still there the change did not reach the build that ran.

## Environment

_To be filled at run time._

## Result

_Not run yet._

## Verdict

_Not run yet._
