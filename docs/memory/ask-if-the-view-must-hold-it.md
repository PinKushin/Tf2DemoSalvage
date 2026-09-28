---
name: ask-if-the-view-must-hold-it
description: A field a view holds only to pass along is the last piece of a refactor, and asking is what finds it.
metadata:
  type: feedback
---

Owner, 2026-08-26, looking at a `MainForm` already reported thin: *"Does the view need to hold them
to pass them on?"* It didn't — `ShowMoment` sampled the demo into two `List<>` fields purely as a side
effect of where the sampling happened. Move the sampling and all three go with it.

**Why:** a field only ever passed along looks like plumbing, so it survives "is this view doing
work?" audits. The narrower question: for each field, who *reads* it — does anything in this class
read it for a reason of its own? A field read only to forward belongs to the far side of the forward.

**How to apply:** at the end of a view refactor, list every remaining field into two piles — state
this view owns, and something being carried. The second pile is the next extraction, and audits
usually miss it. State by name which fields legitimately stay and why (`_timeline` stayed: four
callers still need the decoded demo) — that's the difference between a reader trusting the rest of
the report and not.

**Check whether the seam is constructible before calling it decorative.** `DemoTimeline` has a
private constructor and a `Build` needing a real file's bytes, so sampling one directly was
untestable without shipping a demo into the test project — `IMomentSource` was load-bearing, not
ceremony. [[output-level-assertion-or-it-is-not-done]] is the other half.

Related: [[a-partial-thin-view-is-worse-than-none]].
