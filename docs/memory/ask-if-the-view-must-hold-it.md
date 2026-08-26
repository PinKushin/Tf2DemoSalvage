---
name: ask-if-the-view-must-hold-it
description: A field a view holds only to pass along is the last piece of a refactor, and asking is what finds it.
metadata:
  type: feedback
---

**"Does the view need to hold them to pass them on?"** — the owner, 2026-08-26, looking at a
`MainForm` I had already reported as a thin view.

It did not. `ShowMoment` sampled the demo into two `List<>` fields and handed them to the scene, so
the window held a `DemoTimeline`, a `List<ScenePlayer>` and a `List<SceneProp>` **purely as a side
effect of where the sampling happened**. Move the sampling and all three go with it.

**Why:** a field that is only ever passed along looks like plumbing rather than logic, so it survives
every audit that asks "is this view doing work?" The right question is narrower and mechanical:

> For each field, who *reads* it — and does anything in this class read it for a reason of its own?

A field read only to be forwarded belongs to whatever is on the other side of the forward. The buffers
here were never inspected by the window; it filled them and passed them, which is the definition of
holding something for someone else.

**How to apply:** at the end of a view refactor, list every remaining field and put each in one of
two piles — *state this view owns* (a mode flag, a control, a setting) and *something being carried*.
The second pile is the next extraction, and it is usually the one an audit misses.

**State the fields that legitimately stay, by name and reason.** `_timeline` did not leave in this
change: four callers still need the decoded demo, each to hand it to something in Presentation or
Scene. Saying "the timeline stays, and here is what still needs it" is the difference between a
reader trusting the rest of the report and not. A summary that implies more left than did is worse
than one that claims less.

**The seam is usually an interface, and check whether it is constructible before deciding it is
decorative.** `DemoTimeline` has a private constructor and a `Build` that takes the bytes of a real
file, so anything sampling one directly cannot be tested without shipping a demo into the test
project — which is exactly why that code had no tests. `IMomentSource` was load-bearing, not
ceremony. [[three-test-levels-and-the-third-is-missing]] is the other half: the extraction is only
proved by the real application still drawing.

Related: [[a-partial-thin-view-is-worse-than-none]], [[a-moves-regressions-are-wiring]].
