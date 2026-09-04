---
name: a-picture-is-assertable
description: "Whether it looks right is not answerable by an assertion" is too strong; a specific visual property needs no reference, and open-ended correctness needs a person exactly once to bless a golden image.
metadata:
  type: feedback
---

**This project wrote "whether it looks RIGHT is not answerable by an assertion" into its tests and
its risks, and it is wrong.** The owner's correction, 2026-08-23: "we can use golden image
comparison, or we can check pixels colors and or contrast, although that can be flakey."

Three claims were being run together under one sentence:

- **A specific visual property is assertable now, with no reference image.** "The wall must not show
  through an opaque prop" caught the blend-state leak that had made every static prop translucent
  for two days. "Each pass draws something, and not the same something" caught a mis-wired
  `r_drawworld`. "Three fullbright states produce three different pixels" caught a mode implemented
  as a boolean. None needed a person.
- **Open-ended "does it look right" needs a person exactly ONCE**, to bless a reference. After that
  it is a golden comparison and every later change is assertable. The owner had already said the
  same thing from the other side, about the UI suite's captures: they are "worthless, because we are
  not comparing them to a golden image".
- **Flake is a property of the SETUP, not of the technique.** Driver, resolution and timing make a
  capture vary; a fixed viewport, a fixed tick and a fixed device do not. This project already
  renders offscreen at 64x64 from a fixed matrix and reads exact pixels.

**What the overstatement cost.** `FirstPerson_Capture_WritesAPictureForSomebodyToLookAt` renders the
first-person view and asserts only that a file appeared, on the reasoning above. The viewmodel pass
draws nothing at all — `c_*` models go to the world pass and appear at the eye — and that test
rendered the broken picture, wrote it out, and passed. One mechanical assertion would have caught
it: the viewmodel pass draws more than zero instances when first person is on.

**How to apply:** before concluding a visual claim needs a human, ask what property would differ
between right and wrong and whether it can be measured — count, colour, contrast, "not equal to the
other mode". Reach for "a person decides" only for open-ended correctness, and then bless a
reference so the next person does not have to. And never let "a person decides" stand in for an
assertion that needs no judgement at all.

Related: [[output-level-assertion-or-it-is-not-done]], [[instrument-bugs-outnumber-decoder-bugs]].
