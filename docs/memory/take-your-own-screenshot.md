---
name: take-your-own-screenshot
description: TF2VIEW_CAMERA plus --shot captures any viewpoint without asking the owner; the viewer's own key is F5, demo_gototick takes <tick> 0 1, and every still capture is a PAUSED frame that draws a different pose from playback.
metadata: 
  node_type: memory
  type: reference
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:40:19.132Z
---

**The viewer can be pointed anywhere and photographed without a person at the machine:**
```bash
TF2VIEW_CAMERA="5925.89 -2229.22 474.25 6.50 197.25" \
  pwsh run-exclusive.ps1 <tf2demoview.exe> <demo.dem> --tick 870 --shot out.png
```
`TF2VIEW_CAMERA` is `x y z pitch yaw` — exactly what TF2's own `cl_showpos` prints. `--shot` loads,
seeks, draws, writes, exits — takes the desktop, so it goes inside `run-exclusive.ps1`.

**Why it matters:** this existed for months while an entire evening was spent asking the owner to
press F5 and describe what he saw, where this could have answered in forty seconds. Reading the PNG
settles questions no log can (is a doorway empty, is a prop a quarter turn out).

**How to apply:** the moment a question is about what the screen looks like, capture it — don't reason
from a render log ([[parity-is-the-search-not-the-defence]] has the log line that lied about a
model's position for five rounds). **Point the camera FROM THE DATA** — four blind captures hit walls
before a probe was taught to print where the data actually is. A position known to be in open space
(the recorder's own eye via `--first-person`) is free and always works. See
[[point-the-camera-from-the-data]].

---

## `viewer-screenshots-are-f5` — the key a person presses, and never name one from memory

**F5 takes a screenshot**, captures land in `%LOCALAPPDATA%\Tf2DemoSalvage`. **It was F12 and this
memory said so, wrongly, for weeks** — B214 moved it to F5 for Valve parity (TF2's own `screenshot`
key; F12 collides with replay tips and Steam's overlay). Owner: *"f5 is the shortcut for ss's and the
menu item saying f12 is actually wrong... we cahnged it for valve parity."*

**Why:** the key is TF2's, not this project's — D101, every control follows the binding table.

**How to apply:** never name a key from memory — ask `KeyBindings.KeyFor(...)` or read the defaults.
A key written anywhere else is a copy that goes stale the next time parity moves one (a menu label
once printed "F12" long after the real key was F5).

See [[a-default-is-not-a-constant]], [[no-hardcoded-controls-ever]].

---

## `demo-gototick-relative-is-the-second-argument` — the seek that lands somewhere else

**`demo_gototick <tick> <relative>` is a relative seek**, per the engine's own decompiled handler and
its own syntax message. `demo_gototick 51093 1` sought 51093 ticks FORWARD of wherever the demo
already was, not to absolute tick 51093 — a resulting wrong capture was first read as a real map
divergence before the argument order was checked.

**Correct form for a golden comparison at a specific tick: `demo_gototick <tick> 0 1`** — tick,
relative OFF, pause ON. Always all three arguments explicit.

---

## `paused-is-a-different-sample` — a still capture is a PAUSED frame, and paused draws a different pose

**Pausing clears interpolation for every entity at once**, so it doesn't freeze the picture — it
changes it to the last update's state, by the whole interpolation window (B399). A golden screenshot
from `demo_gototick <tick> 0 1` (paused) compared against an interpolating viewer capture is comparing
poses ticks apart — chased for a long session as a rocket spawn-position bug before this was found.

**How to apply:** any still capture (ours or TF2's) is a PAUSED frame — sample with interpolation off
and compare like with like. A constant positional offset along an entity's travel direction should be
measured in TICKS of that motion before suspecting geometry. Note different entity types can apply
updates at different tick offsets, producing two different-looking errors from one delay. Related:
[[an-entity-index-does-not-name-a-track]]#an-entity-index-is-not-a-real-name,
[[check-at-the-owners-moment]].
