---
name: a-track-walk-is-a-walk-of-the-whole-recording
description: "A per-frame loop over the timeline's tracks visits every entity the demo EVER had (13:1 dead to live on f12); and a diagnostic can cost more than the work it counts."
metadata:
  type: feedback
---

**A loop over `DemoTimeline._props` is a loop over the whole recording, not over what exists.** Stage C's comment called
the sample's closing walk "one reference and one null test per track" as if that were free; f12 has 6,456 tracks for ~492
live props, and the walk cost 0.13 ms a frame (B259, `measurements/2026-10-28-b259-entity-walks.md`). The engine's walks
iterate `m_BaseEntities` (live only); the timeline now keeps that list (`_live`).

**And time the instruments too:** `DrawTally.NotDrawable`, a census, cost 0.065 ms a frame building key strings for ~220
undrawable props that were the same objects every frame — more than half of `collate`'s reconcile walk.

**How to apply:** before calling a per-frame walk cheap, print its COUNT beside the count of what it produces; a large
ratio is the divergence. In a split, time the diagnostics inside their callees like any other part. Related:
[[a-call-count-is-half-an-instrument]], [[count-the-changes-before-caching-on-unchanged]].
