# Measurement 2026-10-23: owned particle dispatches visited while live, against the walk from the first (B577, f12)

**Status: written and committed before the run.** Sections above `## Result` are fixed from here; deviations go under Result.

## Observation

Four f12 steady-frame records (`2026-10-22-f12-steady-frame/A3, B2, B3, C1`) put the burst kinds at: `bursts/dispatches`
0.17-0.23 ms, `bursts/muzzles` 0.18-0.27 ms, and explosions, tracers, blood, splashes and sentries together about 0.04 ms.
`AddParticleDispatches` walked every owned dispatch fired since tick 0 each frame (finding 97, B577). Commit `2ec8bd21`
keeps them in a live list instead; offers are unchanged (unit equivalence test).

## Question

Does `camera particles/bursts/dispatches` fall, by more than the run-to-run spread of one build?

## Prediction

`bursts/dispatches` falls by 0.05-0.2 ms on both scenes at tick 30000 (the walk is linear in fired owned dispatches; the
live ones' attachment queries and offers stay). `bursts/step` and `bursts/muzzles` do not move beyond their spread.
Falsified if the after figure is inside the before build's own spread.

## Method

Two Debug builds: `before` = `01ea58a3` (`fix/per-frame-cursors`), `after` = `2ec8bd21`. Each copied to its own folder
under `D:/measure-f12/` (`b577-before`, `b577-after`). Interleaved runs before/after/before/after per scene, each through
`run-exclusive.ps1`, the quiet gate before each (three `(Get-CimInstance Win32_Processor).LoadPercentage` samples under
25%, no build running):

- empty: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="1613 -2821 154 89 0" tf2demoview.exe
  D:/measure-f12/f12.dem --tick 30000 --measure 20 +fps_max 0`
- gummo: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 tf2demoview.exe D:/measure-f12/f12.dem --tick 30000
  --spectate gummo --first-person --measure 20 +fps_max 0`

The figures read are the per-second `bursts/dispatches`, `bursts/muzzles`, `bursts/step` and `camera particles` parts.

## Control

- Ticks advanced ~1,330 per 20 s window in every run.
- Two runs of one build on one scene give its spread; a difference inside it is not claimed.
- `bursts/dispatches` present and non-zero in the before runs.

## Result

## Verdict
