# Measurement 2026-10-26: finding 105's go/no-go — root bone setup and the forkable HUD

**Status: plan.** Written and committed with the instruments (finding 105 A7.1 + B8.1) before any run.

## Observation

1. Finding 105 designs two within-frame forks on the one worker pool (D228): (A) the roots' `SetupBones` as a pre-pass
   before the draw loop, as `C_BaseAnimating::ThreadedBoneSetup` (`c_baseanimating.cpp:2777-2794`) poses
   `g_PreviousBoneSetups`; (B) the HUD's layout and paint on a worker, overlapping the draw. Both totals in the finding
   are *estimates* (A 0.5-0.7 ms, B 0.3-0.6 ms) conditional on numbers nobody had: setup split root against child, roots
   per frame, the slowest root, the HUD split serial against forkable, and the window a forked paint would have.
2. What was measured: `2026-10-26-threaded-particles-and-sun/02-C-T.txt` (f12 gummo first person, mode 2) prints
   `setup 0.7-1.1` ms of the pose with `built 37-63 … draw 37-63` a rebuild, `posed 36-40`; and `hud 0.69` ms a frame
   with `hud layout`, `hud cards`, `hud paint` among the parts (`HudChat` 0.57 of `hud paint` 0.96 in its first line).
   None of it says how much of setup is roots, nor whether a forked paint would fit before the next frame's `Pack`.

## Question

- A. On f12 gummo first person: how many ms a frame do the **eligible** roots' own `SetupBones` calls cost in the world
  draw loop, over how many builds, and what does the slowest single eligible root cost (the fork's critical path)?
  Eligible is what the pre-pass would take: the engine's `nBoneCount >= 16 && !GetMoveParent()`
  (`c_baseanimating.cpp:2897`), less a ragdoll-driven body (finding 105 A3.4). All roots and the children are reported
  beside it.
- B. How many ms a frame is the HUD's forkable span (HUD layout to paint end, plus `hud tools paint`) against the
  serial half (`hud setup, events, precache, state, scheme, think, gameevents, updates, tools inputs`); how long is the
  window from the overlay's end to the next writer of what the paint reads (the next `Pack`, or on a frame with no
  rebuild the next overlay), separately for the two kinds of frame; and on how many frames, by how much, would the
  forkable span have overrun that frame's own window?
- Go/no-go by finding 105's own thresholds (A6, B7), restated here so the verdict cannot move them:
  - A **go**: eligible roots ≥ 0.4 ms a frame AND ≥ 4 eligible builds a frame. **No-go**: eligible < 0.3 ms.
    0.3-0.4: marginal, reported, not built. Wave 2 (children forked) only if children ≥ 0.4 ms.
  - B **go**: forkable ≥ 0.3 ms AND it fits its window: `hud stall` (summed overrun) under 0.02 ms a frame and stalled
    frames under 5%. Otherwise **no-go**. And B7's prior check: name the top paint children, since doing less beats forking.

## Prediction

- A: eligible roots 0.4-0.8 ms over 10-20 builds a frame; children 0.2-0.4 ms; slowest root 0.03-0.10 ms. So GO, with a
  gain of (eligible − max(slowest, eligible / 5)) − ~0.04 ms fork ≈ 0.3-0.6 ms. Falsified if eligible < 0.4 ms in every run.
- B: forkable 0.5-0.8 ms (paint the larger part), serial 0.1-0.2 ms, packed window 0.8-1.2 ms, stall under 0.02 ms on
  under 5% of frames. So GO. Falsified if forkable < 0.3 ms, or the stall limits are exceeded in any run.

## Method

Instruments (this commit; carried from where timed, B243):

- `BoneSetupMeter` on each scene's `BoneFrameCounter`: every `AnimatingEntity.SetupBones` request times itself, minus
  the time spent in the parent it asks for, and records root (`Follows is null`) or child, eligible or not (root, no
  `Ragdoll`, `BoneCount >= 16`), built or cache hit.
- `EntityModelSet` reads the meter either side of the draw loop's own `SetupBones` call: `SetupRootTicks`,
  `SetupChildTicks`, `SetupRootBuilds`, `SetupChildBuilds`; `SetupEligibleTicks`/`Builds` except where the call's own
  prop is parented without a merge (the engine's move parent, which only that loop sees); and `MaxRootTicks`, the
  largest single eligible build of the pass (one call reaches at most one root). The world pass's counters are what the
  moment line prints: `setup X (roots R over N built, eligible E over M, slowest S peak P; children C over K built)` — R,
  E, C, S means per rebuild, N, M, K builds per rebuild, P the slowest eligible root in the 100-rebuild window.
- `VguiTools` laps `hud tools inputs` (meter, scheme, size, tick) and `hud tools paint` (`SolveTraverse` +
  `PaintTraverse`), both inside the existing `hud tools`. `VguiHud.ForkableTicks` is the one span from the HUD's layout
  start to its paint end, carried; `hud forkable` = that + the tools' paint, per frame.
- At the next overlay's start: `hud window packed` (`MomentScene.PackedAt − last overlay end`, a `Pack` came between) or
  `hud window unpacked` (`this overlay's start − last overlay end`); `hud packed frames` counts the first kind; `hud
  stall` adds `forkable − window` where positive and `hud stalled frames` counts those frames. A count is printed as its
  share of frames (`FrameParts.Count` adds one millisecond). The window spans other phases and is never added into `hud`.

One Release build of this commit: `run-exclusive.ps1 -Shared dotnet build managed/Tf2DemoSalvage.Viewer3D -c Release`,
copied to `D:/measure-f12/offthread-a71/`. Three runs, one at a time, each behind the quiet gate of `run.ps1` here (three
`Win32_Processor.LoadPercentage` samples all under 25%, no other `tf2demoview`/`dotnet`/`MSBuild`/`testhost`):

`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 <exe> D:/measure-f12/f12.dem --tick 30000 --spectate gummo
--first-person --measure 60 +fps_max 0 +r_threaded_particles 1` under `run-exclusive.ps1`; `mat_queue_mode` default
(-1, so 2 on this machine). No comparison is made, so no ABBA: the three runs are repeats, and each figure is quoted as
the range over them.

Reading: steady = the per-second lines after the first five; moment lines after the first one. Frame-weighted means
for the parts; plain means of the moment lines (each is 100 rebuilds). Percent of frame = part ms / that run's
`measured … mean`. Builds per FRAME = builds per rebuild × rebuilds / frames (both printed by the run). Packed window
per packed frame = `hud window packed` / `hud packed frames` share.

## Control

- **The split adds up:** roots + children ≤ setup in every moment line, and close to it — setup is the same calls timed
  inclusively at the call site, so only the meter's own clock reads separate them. Roots + children > setup, or below
  90% of it, voids the instrument. Eligible ≤ roots.
- **Build counts agree with an existing counter:** root builds + child builds per rebuild = `built … draw D` within
  rounding, since both count builds in the draw loop's call — one by `SkeletonPose.PoseBuilds`, one by the meter.
- **The tools split adds up:** `hud tools inputs + hud tools paint ≤ hud tools`; `hud forkable ≥ hud layout + hud cards +
  hud paint`, within rounding (they are laps of the same span). Nested parts are not added together.
- **The window is not empty:** `hud packed frames` must be near rebuilds / frames, and the packed window must be above
  `draw + between` of the same run (it spans at least those); 0 or below says the stamps are not the frames thought.
- **The instruments cost nothing visible:** the meter runs on every request, not only under `--measure`. `setup` and the
  frame mean must fall inside C-T of `2026-10-26-threaded-particles-and-sun` (frame 8.74-8.94 ms, same demo, tick,
  viewport, flags and machine), allowing that branch's later merges; a frame above 9.1 ms in every run says the meter
  costs more than estimated (~3 clock reads per request, ~60-100 requests a frame ≈ 0.005 ms) and the split is suspect.
- Runs agree within 15% on `setup`, or the set is void and rerun.

## Environment

## Result

## Verdict
