---
name: measure
description: >
  Measure the viewer's frame cost honestly - one command, playback seconds, parsed means. Use
  whenever asked how fast the viewer is, whether a change helped, where a frame goes, or before
  and after any performance work. Also use before quoting any frame rate, because the number is
  meaningless without what is NOT being drawn beside it.
---

# Measuring the frame

One call. It builds, takes the machine-wide lock, plays, prints, and exits.

```bash
TF2VIEW_AUTOPLAY=1 pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 \
  managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe \
  <demo> --tick <n> --first-person --measure 20 +fps_max 0
```

Build first — `MSBUILDDISABLENODEREUSE=1 dotnet build managed/Tf2DemoSalvage.Viewer3D` — and read
the build output rather than a grep of it (`error S` is not `error CS`).

## Why each part is there, because each replaces a mistake

- **`--measure <seconds>` counts PLAYBACK, not wall clock.** A run timed from process start spends
  its first twenty seconds on archives and the map, so a "forty second" measurement was two seconds
  of frames — and the thin sample was reported as a result before anyone noticed.
- **It prints to stdout because the log is BUFFERED.** Reading the log while the viewer runs shows
  asset loading and nothing else. That was misread twice, once as the viewer having exited on its
  own.
- **`run-exclusive.ps1` because the viewer takes the desktop**, and a foreground steal ruins whoever
  is looking at the screen.
- **`+fps_max 0`** or the cap is what you measure. The default is 300.
- **It exits itself**, so this never needs backgrounding, a sleep, or a kill.

## Read only a FOCUSED window

`NoFocusSleep` is the engine's own `engine_no_focus_sleep`: an unattended run is clamped and the
number is meaningless. A clamped run is recognisable without knowing that — its phases sum to ~10 ms
under a 63 ms frame with `unaccounted 0`, and a long run comes out bimodal (p25 16 fps, median 106).
`--measure` exits on its own, so run it in the foreground and this does not arise.

## What the lines mean

```
frame rate 235 fps, 6.1 ms; sound, camera, project, advance, capture, hud, draw
moment cost 2.9 ms = sample, drawlist, models, pose; pose = lighting, simulate, setup, skin, anim, rest
                     posed N of M selected, K hidden by pvs
```

- Both are **means over the interval**, never one sampled frame — a per-second sample at 200 fps is
  one frame in two hundred dressed as a measurement.
- `advance` is the scene rebuild; `project` carries the pose since it moved after the view.
- `rest` is a residual. **Every direct column small with `rest` large means the cost is somewhere no
  timer covers yet** — that pattern found 129 ms hiding in a 133 ms pose.
- `posed N of M` is the cull. `N` counts props that actually reached bone setup, not `M − culled`.

## Quoting a number

**Always say what is NOT being drawn** (D129). This viewer has no projectiles, no particles and no
ragdolls, and skinning is on the GPU where Valve's runs on the processor — so a rate compared with
TF2's is comparing a lighter workload, and the gap is worse than the ratio.

TF2's own reference, measured on `cp_badlands` with the GPU at ~30%: **893 fps in a room, 1135–1236
facing a wall — 0.81 to 1.12 ms a frame.** It is CPU-bound and still sub-millisecond. Different map
from ours, so treat it as an order of magnitude rather than a paired measurement.

## An empty view is the interesting one

Point the camera at nothing and measure again:

```bash
TF2VIEW_CAMERA="1613 -2821 154 89 0"
```

An engine's frame collapses when there is nothing to draw. Ours has a floor — `sample`, `drawlist`,
`models` and placement all walk every prop regardless of visibility (B259). If a change is meant to
reduce work, the empty view is where it shows.
