---
name: real-data-hides-bugs-small-inputs-expose
description: Two bugs here were invisible on real files and died to a three-shape fixture; density is what conceals them.
metadata:
  type: feedback
---

A bug that a real map or a real demo cannot expose is not a rare bug — it is a bug whose symptom
is *cancelled by the size of the input*. Two of them, found 2026-08-12:

- The map-clustering code marked grid occupancy at segment **endpoints** only, so a long edge
  skipped the cells between and split one connected map into pieces. On a real map, vertex density
  means some vertex lands in almost every cell, so it never split anything. Three quads with
  500-unit edges split immediately.
- A test asserting `playlist.Items.Count` continued to pass after the control moved to virtual
  mode, where `Items` is empty — it happened to still be right for a different reason, but nothing
  in the test said which reason it was measuring.

**Why:** the density of real data supplies the missing behaviour by accident. The measurement then
reports "correct" while measuring nothing, which is the failure mode described in
[[fixtures-are-the-weak-point]] approached from the other side — there the fixture was wrong, here
the fixture is the only thing that can be right.

**How to apply:** when a rule is derived from a measurement over real files (see
[[differential-beats-fixtures]]), write the unit test at the smallest size where correct and broken
differ, not at a realistic one. Ask what the real input is *supplying* that a minimal one would
not. If the answer is "enough points that the gap never appears", that is the test to write.

Related: [[measure-the-output-not-the-capability]].
