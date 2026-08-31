---
name: take-your-own-screenshot
description: TF2VIEW_CAMERA plus --shot captures any viewpoint without asking the owner; use it the moment a question is visual.
metadata:
  type: reference
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

The screenshot KEY is F5 ([[viewer-screenshots-are-f5]]); this is the same picture without a person.
