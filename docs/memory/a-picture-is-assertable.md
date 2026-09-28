---
name: a-picture-is-assertable
description: "\"Whether it looks right is not answerable by an assertion\" is too strong; a specific visual property needs no reference, and open-ended correctness needs a person exactly once to bless a golden image."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 71bbc8e9-0f7a-489c-a987-3e0867aae1fa
  modified: 2026-09-10T22:54:57.543Z
---

**"Whether it looks RIGHT is not answerable by an assertion" was written into tests and risks, and is
wrong.** Owner, 2026-08-23: *"we can use golden image comparison, or we can check pixels colors and
or contrast, although that can be flakey."*

Three separate claims were run together:
- **A specific visual property is assertable now, no reference needed** — "the wall must not show
  through an opaque prop" caught the two-day blend-state leak; "each pass draws something, not the
  same something" caught a mis-wired `r_drawworld`; "three fullbright states produce three different
  pixels" caught a mode implemented as a boolean.
- **Open-ended "does it look right" needs a person exactly ONCE**, to bless a reference — after that
  it's a golden comparison. The owner said the mirror of this about the UI suite's captures: they're
  "worthless, because we are not comparing them to a golden image".
- **Flake is a property of the SETUP, not the technique** — driver/resolution/timing vary; a fixed
  viewport, tick and device don't. This project already renders offscreen at 64x64 with exact pixel
  reads.

**What the overstatement cost:** `FirstPerson_Capture_WritesAPictureForSomebodyToLookAt` asserted
only that a file appeared. The viewmodel pass drew nothing (`c_*` models went to the world pass
instead) and the test rendered the broken picture and passed. One mechanical assertion (viewmodel
pass draws >0 instances in first person) would have caught it.

**How to apply:** before deciding a visual claim needs a human, ask what property would differ
between right and wrong, and whether it's measurable (count, colour, contrast, "≠ other mode").
Reach for "a person decides" only for open-ended correctness, then bless a reference.

Related: [[output-level-assertion-or-it-is-not-done]], [[instrument-bugs-outnumber-decoder-bugs]].

## But a VIEWER SCREENSHOT is not a diffable artefact — measured 2026-09-04

Two captures of the same code/demo/tick differ in bytes. Found correctly: a shader change's before/
after PNGs differed, but so did the CONTROL (re-running the unchanged build) — so "different" proved
nothing. At least one confound is visible (fps overlay prints a per-run number); `+cl_showfps 0`
produced no file at all while `cmp` reported "different" for two nonexistent paths — two instrument
faults in one check.

**So `--shot` is for LOOKING, not diffing.** For "does this change any pixel", use `Rendering.Tests`'
offscreen harness (`OffscreenTarget` + `DrawModelPose`, fixed camera, deterministic pixel read). Its
limit: it draws from a loaded `MapAssets` (sealed, `private init`), so a test can't build one with
chosen material state — only load a real map and use what's on it. If no map exercises the branch,
there's no pixel test to write (`$phongexponenttexture`, B334: cp_process_final resolves zero
exponent maps, a real demo resolves 21). See [[instrument-bugs-outnumber-decoder-bugs]]#run-the-control-before-arguing.
