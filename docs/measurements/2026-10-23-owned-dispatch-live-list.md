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

Run order per scene: b1, a1, b2, a2. The quiet gate refused 38 times in all, and every run started under 25% load. Raw
outputs were kept in the session scratch directory and not committed. The figures below are per-second lines: min-max,
then the median.

| run | load before / after | end tick | `bursts/dispatches` | `bursts/muzzles` | `bursts/step` | `camera particles` med |
|---|---|---:|---|---|---|---:|
| empty b1 | 23,15,14 / 44 | 31,282 | 0.12-0.54, 0.20 | 0.07-0.67, 0.26 | 0.32 | 1.64 |
| empty a1 | 24,16,11 / 46 | 31,283 | 0.11-0.65, 0.21 | 0.08-0.48, 0.24 | 0.31 | 1.72 |
| empty b2 | 8,7,13 / 17 | 31,282 | 0.13-0.54, 0.21 | 0.08-0.71, 0.24 | 0.32 | 1.72 |
| empty a2 | 14,17,16 / 52 | 31,280 | 0.12-0.61, 0.21 | 0.08-1.27, 0.24 | 0.35 | 1.82 |
| gummo b1 | 14,6,11 / 27 | 31,288 | 0.12-0.63, 0.21 | 0.09-0.40, 0.21 | 0.38 | 2.67 |
| gummo a1 | 17,18,19 / 12 | 31,292 | 0.11-0.75, 0.20 | 0.11-0.67, 0.25 | 0.36 | 2.58 |
| gummo b2 | 11,12,11 / 30 | 31,289 | 0.12-0.73, 0.20 | 0.10-0.40, 0.21 | 0.38 | 2.78 |
| gummo a2 | 15,13,16 / 20 | 31,284 | 0.11-0.55, 0.19 | 0.09-0.39, 0.22 | 0.35 | 2.81 |

Controls: every run played about 1,280-1,290 ticks, and `bursts/dispatches` was non-zero in both before runs.

## Verdict

**Falsified. `bursts/dispatches` does not move:** the medians are 0.20-0.21 ms before and 0.19-0.21 ms after on both
scenes, inside each build's own spread. The walk from the first owned dispatch was not where the time went at tick 30000.
What remains is the per-frame work on the live offers: an attachment query for each, a dormancy closure allocated for
each, a `ParticleBurst` for each, and the walk over the 660-tick window of entity-less dispatches with a name lookup per
dispatch. Which of these costs the time is **not split**. The change stays, as an O(match) → O(live) fix that changes no
offer, but it saves nothing measurable here.
