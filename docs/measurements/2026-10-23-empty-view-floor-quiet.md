# Measurement 2026-10-23: the viewer's empty-view floor, repeated on a quieter machine (f12)

**Status: written and committed before the run.** Sections above `## Result` are fixed from here; deviations go under Result.

## Observation

`2026-10-22-empty-view-budget-ldr-f12.md` read the viewer's empty view (f12, camera `1613 -2821 154 89 0`, tick 30000) at
7.29 ms/frame, but at 80-100% CPU load with another agent's compiler running; its verdict says repeat on a quiet machine
before quoting it.

## Question

What is the empty-view floor, and its per-phase shape, at low machine load? If it is well under 3 ms, the loaded number
misled and the floor is not the gap.

## Prediction

5-6 ms (the loaded figure divided by ~1.3), same shape: camera largest, then hud and advance.

## Method

Build `origin/main` `273d7c57` + this record, Debug. Two runs back to back, each through
`run-exclusive.ps1` (waiter on `ACQUIRED`):
`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="1613 -2821 154 89 0" tf2demoview.exe D:/measure-f12/f12.dem
--tick 30000 --measure 20 +fps_max 0`, and one with `--spectate gummo --first-person` instead of the camera. Load
`(Get-CimInstance Win32_Processor).LoadPercentage` read before and after each run.

## Control

- Ticks advanced ~1,333 per 20 s window (instrument).
- The two empty runs within 10% of each other, else the spread is stated and no single figure is quoted.
- Load under 30% at both reads, else the run is labelled loaded.

## Result
