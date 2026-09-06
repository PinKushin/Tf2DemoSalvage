---
name: two-margins-are-not-the-table
description: Counting type and orientation separately made a map look like it had 324 fixed sprites; it has none, and 324 models.
metadata:
  type: feedback
---

A census reported `cp_granary`'s detail props as two margins — *19,189 screen-aligned, 324 fixed* —
and that was read as "324 fixed sprites". Granary has **no** fixed-orientation sprite: its 324
fixed-orientation objects are `DETAIL_PROP_TYPE_MODEL`, a studio model nothing here draws. The two
variables correlate perfectly on that map and not at all on `koth_harvest_final`, so one map's
margins cannot be read as the other's.

The claim shipped into three documents before a screenshot of the wrong hillside sent me back to the
probe output — where the tell had been sitting all along: those 324 rows printed a `m_flScale` of
**−181,657,600**. A model does not use that field. **A nonsense number beside a plausible one is the
instrument saying which rows it should not have selected.**

**Why:** margins lose the interaction, and the interaction is usually the finding. "How many are
screen-aligned" and "how many are sprites" answer different questions, and the one that decides what
gets drawn is the cross.

**How to apply:** when two categorical fields both gate the same behaviour, print the CROSS, not the
two totals — one line per (type, orientation) pair. And read every column of a probe's own output
before quoting one of them: an impossible value in a neighbouring field is evidence about the row.
Related: [[the-denominator-decides-what-can-be-lost]],
[[instrument-bugs-outnumber-decoder-bugs]], [[print-a-value-somebody-can-recognise]].
