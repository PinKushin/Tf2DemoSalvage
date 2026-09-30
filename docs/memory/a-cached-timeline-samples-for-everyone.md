---
name: a-cached-timeline-samples-for-everyone
description: DemoTimeline.PropsAt keeps a sample between calls, so parallel corpus tests sharing TimelineCache's timeline read each other's state.
metadata:
  type: project
---

`PropsAt` advances a persistent sample (`_sampleSynced`, `_sampledTo`, `_props`; B259's incremental rebuild), so a
`DemoTimeline` is NOT read-only once built — `TimelineCache`'s remark says it is. Corpus.Tests runs `ParallelScope.All`,
so two tests sampling one cached timeline at once corrupt each other's answer.

**Why:** 2026-09-30 (B105 `model_world`): four parallel `[TestCase]`s of one test on the cached z1800 — three found no
sapper at ticks where the `item-props` census, and each case run alone, found it. It read as a decode gap.
**How to apply:** a corpus test that calls `PropsAt` builds its own timeline (`DemoTimeline.Build`) and walks its ticks
in order inside ONE test; never `[TestCase]` rows over a shared one. An absence only under parallel runs is this, not
the demo. See [[instrument-bugs-outnumber-decoder-bugs]].
