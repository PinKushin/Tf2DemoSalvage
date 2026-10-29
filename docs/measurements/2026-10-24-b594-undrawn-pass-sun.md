# B594: what the first-person undrawn pass costs, and how many sun calls each pass makes (2026-10-24)

## Observation

The main-thread profile (`2026-10-24-main-thread-cpu-profile.md`) put 5.8% exclusive in `LevelLighting.SunFrom`. The
first-person undrawn pass (`MomentScene.Pose`) lights with the UNCACHED `LightingAt`/`SunAt`; the world pass goes
through the light cache (`ModelLightingAt`/`ModelSunAt`, at most `lightcache_maxmiss` builds a frame). A Fable code-read
estimated 0.2-0.4 ms for the pass (not measured). Earlier `--measure` runs already print `undrawn 0`/`0.1` ms on f12
gummo first person (`2026-10-21-struct-copies-and-allocations/alloc-fp.txt`), rounded to 0.1.

## Engine read (source)

`CViewRender::DrawMonitors` (`viewrender.cpp:3240-3293`) force-draws the local TF player (`ForceTempForceDraw`) only
when an active, non-dormant `point_camera` exists; otherwise `C_BasePlayer::ShouldDraw` is false in first person and no
`DrawModel`, so no `SetupBones` and no light-cache lookup, reaches him. Ours already gates the pose on the device's
`DrawsMonitor` (B511: `PosesUndrawn`, `EntityModels.Instances.cs:188`): without a monitor the pass simulates and
returns before collation and lighting. The only consumer of its instances is `device.MonitorOnlyModels`
(`ViewerPresenter.Device.cs:217`).

## Question

On f12 gummo first person, how many props does the undrawn pass get, does it build instances (is a monitor drawing),
how many `SunFrom` calls does it make per frame against the world pass, and what does it cost in ms?

## Prediction

If `cp_`/f12's map has no active camera target, `instances 0` and `sun 0` for the undrawn pass and its ms is the
simulate of ~5 props, under 0.1 ms: then B594's lighting lead is moot on f12 and the 5.8% is not this pass. If a
monitor draws, ~5 instances and ~5 sun calls per frame against ≤2-3 from the world pass.

## Method

`MomentPhases.UndrawnCensus` (new): `LevelLighting.SunCalls`, incremented inside `SunFrom`, differenced across the world
pass and across the undrawn pass; the props the pass was handed; `UndrawnInstances.Count` after it. Printed on the
`moment cost` line beside `undrawn` (now three decimals). Release build via `run-exclusive.ps1 -Shared`; runs via
`run-exclusive.ps1`, quiet gate before each:

- gummo: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 tf2demoview D:/measure-f12/f12.dem --tick 30000
  --spectate gummo --first-person --measure 30 +fps_max 0`
- empty: no `--spectate`/`--first-person`, `TF2VIEW_CAMERA="1613 -2821 154 89 0"`.

## Control

The empty view is not first person, so its undrawn props must be 0 and its undrawn sun 0. The unit control:
`MomentSceneTests.Pose_InFirstPersonWithAMonitorOrNot_BuildsTheFollowedPlayerOnlyForOne` asserts props 1 both ways and
instances 1/0 with/without a monitor.

## Result

Release, branch `perf/b594-undrawn-pass`. One run per view (a census of counts; nothing was changed, so no
before/after pairs). Per-line values are means over 100 rebuilds.

- **Instrument fault found first:** the first gummo run read `0 props, 0 instances, sun 0 vs world 0` on every line —
  the world's zero was the control that said the instrument was broken. `MomentPresenter.PoseNow` rebuilds the phases
  with `_built with { … }` and did not copy the new field (the same omission its comment records for `Unjudgeable`).
  Fixed; the numbers below are from the second run.
- **gummo first person** (3323 frames, mean 9.03 ms): **undrawn 0.023-0.027 ms, 6 props, 0 instances, 0 sun calls** on
  every line; world-pass sun calls **0.02-2.0 a frame** (light-cache misses, at the `lightcache_maxmiss` ceiling at worst).
- **empty view** (8656 frames, 3.47 ms): undrawn 0, 0 props, 0 sun — the control holds.

**Reading.** The prediction's first branch: f12's map draws no monitor, so B511's gate already stops the undrawn pass
before collation, posing and lighting — the uncached `LightingAt`/`SunAt` are never called. What remains is simulating
6 props (0.025 ms), which the engine also does (it animates the local player). **The 0.2-0.4 ms estimate is refuted;
the saving available on f12 is zero.** And `SunFrom` runs at most ~2 times a frame, so its 5.8% profile share is either
a slow trace (≈0.25 ms each, if real) or sampling bias — still open, and not the undrawn pass.

## Verdict

Confirmed, first branch of the Prediction. On f12 gummo first person the undrawn pass is handed 6 props (predicted ~5),
builds 0 instances and makes 0 sun calls, at 0.023-0.027 ms (predicted under 0.1 ms); the empty-view control read 0 props,
0 sun, 0. B594's lighting lead is moot on f12: the saving available there is zero, and the Fable code-read estimate of
0.2-0.4 ms is refuted. The 5.8% exclusive share of `SunFrom` in the profile is not this pass (the world pass makes 0.02-2.0
calls a frame) and stays open.

Verdict written 2026-10-29 after the fact from the Result above; the run itself was not repeated.
