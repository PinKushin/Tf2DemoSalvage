---
name: take-your-own-screenshot
description: TF2VIEW_CAMERA plus --shot captures any viewpoint without asking the owner; use it the moment a question is visual.
metadata: 
  node_type: memory
  type: reference
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:40:19.132Z
---

**The viewer can be pointed anywhere and told to photograph it, without a person at the machine:**

```bash
TF2VIEW_CAMERA="5925.89 -2229.22 474.25 6.50 197.25" \
  pwsh run-exclusive.ps1 <tf2demoview.exe> <demo.dem> --tick 870 --shot out.png
```

`TF2VIEW_CAMERA` is `x y z pitch yaw` — exactly the numbers TF2's own `cl_showpos` prints, so a
coordinate the owner reads out of the game reproduces the same frame here. `--shot` loads, seeks,
draws, writes the PNG and exits. `--tick` says when. It takes the desktop, so it goes inside
`run-exclusive.ps1`.

**Why this matters more than it sounds:** it existed for months, built for parity captures, and an
entire evening was spent asking the owner to press F5 and describe what he saw — while every
question was one this could have answered in forty seconds. Reading the PNG back also settles
questions no log can: whether a doorway is empty, whether a prop is a quarter turn out.

**How to apply:** the moment a question is about what the screen looks like, capture it. Do not
reason from a render log — see [[parity-is-the-search-not-the-defence]] for the log line that lied
about a model's position for five rounds. A capture is also how a fix is verified: the same
viewpoint before and after, against the game's own screenshot.

---

## `viewer-screenshots-are-f5` — the key a person presses, and never name one from memory

**F5 takes a screenshot in the viewer**, and captures land beside the log in
`%LOCALAPPDATA%\Tf2DemoSalvage` as `shot-<yyyyMMdd-HHmmss-fff>.png`.

**It was F12 and this memory said so, which was wrong for weeks.** B214 moved it to **F5 for Valve
parity** — F5 is TF2's own `screenshot` key, so a config that rebinds screenshots moves the viewer's
with it. F12 was a bad choice twice over: TF2 gives it to replay tips and Steam's overlay takes it as
well. The owner, correcting it: *"f5 is the shortcut for ss's and the menu item saying f12 is
actually wrong"* … *"we cahnged it for valve parity"*.

**Why:** the key is not a fact about this project, it is TF2's. That is the whole of D101 — every
control comes from the binding table, and the table follows the game.

**How to apply:** never name a key from memory. Ask `KeyBindings.KeyFor(ViewerAction.Screenshot)`,
or read the defaults in `ViewerAction.cs`. A key written down anywhere — a memory, a menu label, a
message to the owner — is a copy that will go stale the next time parity moves one, and B239 is what
that costs: the menu printed "F12" long after the key was F5, because a LABEL is not a registration
and nothing breaks when it lies.

See [[a-default-is-not-a-constant]] and [[no-hardcoded-controls-ever]].
