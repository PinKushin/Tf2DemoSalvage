---
name: tf2-measure
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
  of frames — and the thin sample was reported as a result before anyone noticed. **The window starts after the
  opening seek** (finding 82): until 2026-10-19 it counted the `--tick` seek, about 8 of 20 seconds on f12. Control:
  ticks advanced should be about 66.7 × seconds.
- **`between`** is time outside the frame sequence (idle loop, message pump); **`gc:`** is the runtime's own pause,
  collections and allocation per frame — a pause lands in whichever phase allocated, so no phase names it.
- **It prints to stdout because the log is BUFFERED.** Reading the log while the viewer runs shows
  asset loading and nothing else. That was misread twice, once as the viewer having exited on its
  own.
- **`run-exclusive.ps1` because the viewer takes the desktop**, and a foreground steal ruins whoever
  is looking at the screen.
- **`+fps_max 0`** or the cap is what you measure. The default is 300.
- **It exits itself**, so this never needs backgrounding, a sleep, or a kill.

## Per-pass GPU needs the window off screen

Clear, Sky3D and World per-pass GPU differ by whether any part of the viewer window is visible; the frame mean does not.
For any per-pass GPU comparison prefix `TF2VIEW_WINDOW_POS=-3000,100` on every run and classify each run by Clear GPU
(above 1.0 is the nothing-visible state, below 0.3 the visible one). Evidence: `docs/measurements/2026-10-30-gpu-state-selector.md`.

## The focus sleep is off for you

`--measure` holds `engine_no_focus_sleep` at 0 where the sleep is read, over the command line, saved
settings and any `.cfg` run later; logs `engine_no_focus_sleep N ignored while measuring` once per
value. Agent-launched viewer never takes focus; the 50 ms sleep clamped every run
(`ViewerSessionTests.Idle_WhileMeasuringWithANoFocusSleepSet_NeverSleepsAndSaysSoOnce`). Do not pass it by hand. A clamped
run, should one ever turn up again, looks like phases summing to ~10 ms under a 63 ms frame with
`unaccounted 0`.

## What the lines mean

```
frame rate 235 fps, 6.1 ms; sound, camera, project, advance, capture, hud, draw
moment cost 2.9 ms = sample, drawlist, models, pose; pose = lighting, simulate, setup, skin, anim,
                     reports, collate, baked, place, undrawn, advance (corpses …), rest
                     posed N of M selected, K hidden by pvs
```

- Both are **means over the interval**, never one sampled frame — a per-second sample at 200 fps is
  one frame in two hundred dressed as a measurement.
- `advance` is the scene rebuild; `project` carries the pose since it moved after the view.
- `collate` is the leaf/frustum cull, `baked` a static prop's colour mesh and lighting state,
  `place` everything after the skin up to the instance, `undrawn` the first-person player's own
  pass, `advance` the corpse environment whole — `corpses` is the stepping inside it, so it is not
  added to the sum. `anim` sits inside `setup`.
- `rest` is a residual (`MomentPhases.PoseRest`, the one formula both lines read). **Every direct column small with `rest` large means the cost is somewhere no
  timer covers yet** — that pattern found 129 ms hiding in a 133 ms pose.
- `posed N of M` is the cull. `N` counts WORLD-pass props that reached bone setup, not `M − culled`. Before
  2026-10-17 it was read after the first-person undrawn pass and counted that pass instead ("posed 5" on f12).
- `built B (simulate S, corpses C, draw D, place P)`: skeletons built, each counted at its site; B ≈ posed is one build
  per prop; B above the sum means a site no counter covers.
- Rate line ends `; parts:` (only under `--measure`): `camera view/setcamera/pose/particles`, then hud parts. Nested:
  `hud frame` ⊃ `hud think…hud paint`; `hud paint` ⊃ viewport children by `.res` name; `specgui` ⊃ `playerpanelN`;
  `card.*` sums one child name over all cards. Do not add nested parts together.

## Quoting a number

**Always say what is NOT being drawn** (D129), and check it against the code the day you measure: the
2026-09 list ("no particles, no ragdolls") went stale. Ragdolls are simulated and drawn (B58) and particles
are drawn (D221), so the workload is no longer lighter by those. Skinning is on the GPU where Valve's runs
on the processor. Muzzle flashes, shell ejection and `DoAnimationEvents` were undrawn when D129 was written
and are unchecked since. Projectiles are drawn (owner sees them; no class of their own found).

TF2's own reference, measured on `cp_badlands` with the GPU at ~30%: **893 fps in a room, 1135–1236
facing a wall — 0.81 to 1.12 ms a frame.** It is CPU-bound and still sub-millisecond. Different map
from ours, so treat it as an order of magnitude rather than a paired measurement.

## An empty view is the interesting one

Point the camera at nothing and measure again:

```bash
TF2VIEW_CAMERA="1613 -2821 154 89 0"
```

**Without `--first-person`** — it follows the player and ignores the override (finding 82). An engine's frame
collapses when there is nothing to draw. Ours has a floor — `sample`, `drawlist`,
`models` and placement all walk every prop regardless of visibility (B259). If a change is meant to
reduce work, the empty view is where it shows.
