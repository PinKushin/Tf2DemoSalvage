# Muzzle-flash log line off the main thread's hot path (B591, 2026-10-24)

## Observation

The main-thread profile (`docs/measurements/2026-10-24-main-thread-cpu-profile.md`, run G) put 1.5% inclusive of
main-thread samples in `StreamWriter.Flush` under `FileLogWriter.Write`. The line being written is
`BurstEffects.StepOver`'s "viewmodel muzzle flash …", logged at Information once per first-person flash and flushed
per line on the main thread: the B191 shape.

## Question

Does writing that line only at Debug, and building it only when Debug is enabled, change the f12 gummo
first-person frame?

## Prediction

At most ~0.1 ms a frame on gummo first person (1.5% of a perturbed ~10 ms of work, and flashes are bursty, so the
whole-run mean is smaller), which is inside run-to-run spread: **a null result on the frame is the expected
outcome**. The empty view has no first-person flashes, so it must not move (the control).

## Method

- Builds: Release, `dotnet build managed/Tf2DemoSalvage.Viewer3D -c Release` via `run-exclusive.ps1 -Shared`; the
  BEFORE binary is the branch with the two `BurstEffects.cs` edits reverted, the AFTER binary with them; each copied
  to the scratchpad so the two can be interleaved.
- Runs, interleaved B/A, three of each per view, through `run-exclusive.ps1`, a quiet gate (`Win32_Processor`
  load) before each:
  - gummo: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 tf2demoview D:/measure-f12/f12.dem --tick 30000
    --spectate gummo --first-person --measure 30 +fps_max 0`
  - empty: same without `--spectate`/`--first-person`, `TF2VIEW_CAMERA="1613 -2821 154 89 0"`.
- Read: the `frame rate` ms and the `hud`/`camera particles` parts from stdout.

## Control

- The empty view (no viewmodel flashes) is the control: any B/A difference there is noise.
- Unit: `BurstEffectsStepTests.StepOver_AViewmodelFlashWithDebugDisabled_NeverCallsTheLogger` (logger never called
  below Debug) and `…WithDebugEnabled_LogsItsLineAtDebug` (the line still exists for diagnostics). Both were red
  before the edit.

## Result

(filled in after the runs)
