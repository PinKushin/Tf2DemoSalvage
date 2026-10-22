# The corpse worker's and the beams' allocations (finding 90)

## Observation

Finding 89's allocation trace (`2026-10-21-struct-copies-and-allocations.md`, f12 tick 30000, first person) left, in MB
per 20 s: `CorpsePhysics.Advance` 1,364 on the corpse worker, of which `Nullable<IvpMatrix>[]` 241; a corpse's
`SceneProp` copy in `UpdateClientSideAnimations` 306; `PoseLayer[]` 273; `PlacementOf`'s `float[]` 172; on the render
thread the heal-beam closure 173 and `VguiRenderer.BuildVertices` 99 (B574). Six fixes were made against those sites,
each with a zero-allocation or reuse test that was red first.

## Question

1. Per site, how many MB per 20 s does each allocate before and after the six fixes?
2. Do render-thread MB a frame, every-heap MB a frame, the collector's pause, the mean frame and the frames over
   100 ms in a 60 s run move?
3. Is the picture the same?

## Method

- Before: `e1ec318a` (a detached worktree at `D:\n90-base`). After: this branch's last fix commit.
- Each side built with `MSBUILDDISABLENODEREUSE=1 dotnet build managed/Tf2DemoSalvage.Viewer3D` (Debug) in the same
  locked command that runs it.
- Allocation: `dotnet-trace collect --profile gc-verbose` over `tf2demoview <f12> --tick 30000 --first-person
  --measure 25 +fps_max 0`, read by `alloc-trace <trace> 4 all 200 220 any` for the type table, and with the root
  argument `CorpsePhysics.Advance` for the corpse subtree. Same probe arguments both sides.
- Timing: `--measure 60` three times per side, first person, same tick.
- Equivalence: `--shot` at first-person ticks 30000, 30540, 31000, before twice and after once, compared with
  `pair-diff`.
- A run is void when its frame count is clearly below its siblings' or it shows a heartbeat gap; void runs are rerun
  once and said so.

## Control

- `alloc-trace`: the trace's render-thread MB per 20 s / frames must be near that run's `--measure` render-thread bytes
  a frame; the corpse worker's `CorpsePhysics.Advance` root must appear on both sides (a missing root means the probe
  lost the thread, not that the allocations went).
- Unchanged sites must stay put: `IvpContactRecord` (not touched) should read about the same on both sides.
- `pair-diff`: before against before is the control; a difference outside the fps text box (y 2-10) there voids it.

## Environment

## Result

## Verdict
