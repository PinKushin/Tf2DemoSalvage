---
name: log-what-is-about-to-be-drawn
description: Verify with a run before the long suite, and log scene composition so a run can answer
metadata:
  type: feedback
---

**Run the app before running the suite.** The corpus suite takes seven minutes and the Viewer3D
suite nine; a launch takes fifteen seconds and has caught defects the suite could not. Stated
2026-08-13 after the suite twice failed to show what a single launch showed immediately.

**And that only works if the log says what is about to be drawn.** The viewer logged map, asset
and render counts but nothing about the scene, so "every player is grey" was invisible in the log
and had to be noticed by eye. One line fixes it:

```
roster: 6 red, 6 blu, 1 watching, 0 unknown, 12 of 13 with a class
12 players drawn at the midpoint of the demo
```

A team colour that never arrives reads as `0 red, 0 blu` the moment a file opens.

**Why:** counts of what the code is about to draw are the cheapest possible instrument, they cost
nothing per frame when written once per load, and they turn a class of defect from "something looks
wrong, go and investigate" into a number that is obviously wrong on sight.

**How to apply:** when adding anything that draws, log its composition once per load — how many of
each kind, how many skipped, how many unknown. Prefer a launch for the first check and the suite
for the guarantee. Related: [[measure-the-output-not-the-capability]],
[[instrument-bugs-outnumber-decoder-bugs]], [[ui-tests-run-every-time]].
