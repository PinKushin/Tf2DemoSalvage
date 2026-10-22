# Measurement 2026-10-22: what is inside the `camera` phase on f12

## Observation

`2026-10-22-f12-steady-frame.md` measured the steady f12 frame at 14-15.5 ms, of which the `camera` phase is 8.4-9.5 ms
(59-62%). Its raw `parts:` lines already split `camera` into `view`, `setcamera` (with the device's own split), `pose` and
`particles` (with the effects' own split), and the `moment cost` lines split one rebuild of the moment; but nobody has read
those lines together, and in A2's heaviest second `camera pose` was 7.37 ms against a `moment cost` of about 4.6 ms, so the
pose may hold time the moment's own split does not name.

## Question

On lcor's f12 (`demostf-cp_process_f12-2026-08-08-2207.dem`), `--tick 30000 --first-person`, 1280x720, `+fps_max 0`,
built from this branch's base `311a765d`: (1) how do the steady `camera` milliseconds divide between `view`, `setcamera`
(and its parts), `pose` and `particles` (and theirs); (2) how much of `camera pose` is the moment rebuild's own `moment cost`
split, and what holds the rest; (3) which single named part is the largest, and does the engine pay for that work?

## Method

The `--measure` phase line, its `parts:` list and the `moment cost` line, all printed by the viewer from the clock readings
the code itself used (B243); nothing recomputed here. Two runs, R1 and R2, back to back:

```
TF2VIEW_AUTOPLAY=1 pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 \
  managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe \
  C:/Users/pinku/source/repos/PinKushin/Tf2DemoSalvage/tools/corpus/local/demostf-cp_process_f12-2026-08-08-2207.dem \
  --tick 30000 --first-person --measure 20 +fps_max 0
```

CPU load (`Get-CimInstance Win32_Processor` LoadPercentage, 3 x 2 s) before and after each run. Raw output in
`2026-10-22-f12-camera-breakdown/`. Steady = the per-second lines after the first five. If (2) leaves more than 1 ms of
`pose` unnamed, a timer is added around the unnamed calls (carried, not recomputed) and the pair repeated as R3, R4; that
addition is recorded here before R3 runs.

## Control

- **Sum control:** `camera view + camera setcamera + camera pose + camera particles` must be within 5% of the `camera`
  phase column on the same line; if not, the parts are not timing the phase they claim to split.
- **Sub-split control:** each `setcamera` part list and `particles/*` list must sum to no more than its parent (they are
  nested intervals of the same clock); a sum above its parent means a part is counted twice.
- Ticks advanced about 66.7 x 20; the run reports particles and model decals (a run with no demo loaded reports none).
- R1 and R2 agree within 15% on `camera`, or the pair is void and repeated with the load recorded.

## Environment

## Result

## Verdict
