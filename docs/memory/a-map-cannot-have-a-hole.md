---
name: a-map-cannot-have-a-hole
description: A gap is never in the map; name which of the parallel readings of it has the gap.
metadata:
  type: feedback
---

The owner, 2026-09-07, cutting an investigation short: *"they literally cant have holes because
hammer doesnt allow holes, so idk what you mean by holes"*.

**Why:** a `.bsp` is compiled from sealed solids, so the geometry is not the thing with a gap in it.
This project holds at least four parallel readings of one map — physics ledges from
`LUMP_PHYSCOLLIDE`, terrain rebuilt from `LUMP_DISPINFO`, the brush tree from `LUMP_BRUSHES`, and
whatever a probe assembles out of those. Saying "the map has a hole" collapses all four into a claim
about Valve's data, which is the one place the fault cannot be.

**How to apply:** name the reading and give it a denominator out of the file before reporting a gap.
Doing that here took one measurement: `LUMP_BRUSHES` declares 2,722 solid brushes,
`LUMP_PHYSCOLLIDE` yields 2,671 ledges for the same solid, vbsp writes one convex per referenced
brush — so the physics reading is complete, and the gaps were in the CAMERA's reading, which stops
on displacement base brushes that vphysics deliberately has no collision for. Two readings of one
map are *supposed* to disagree there.

Same shape as [[an-empty-search-needs-a-control]] one level up: that one says an absence is usually
about the grep, this one says an absence is usually about which reading you asked. See also
[[instrument-bugs-outnumber-decoder-bugs]] — the probe's own seed defaults were wrong here too.
D149.
