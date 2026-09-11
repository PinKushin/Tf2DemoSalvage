---
name: a-map-cannot-have-a-hole
description: A gap is never in the map; name which of the parallel readings of it has the gap.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T03:56:18.056Z
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

Same shape as [[instrument-bugs-outnumber-decoder-bugs]]'s empty-search rule one level up: that one
says an absence is usually about the grep, this one says an absence is usually about which reading
you asked. The probe's own seed defaults were wrong here too. D149.

---

## `a-hole-is-not-always-a-drawing-fault` — ask what CLASS of geometry could occupy it

Black areas in the map view were chased through three shading explanations and one culling
explanation before the cause turned out to be **static props** — `prop_static` placements in the
BSP game lump (35, `sprp`), which this project did not read at all.

`tools/toolsinvisibledisplacement` is collision-only terrain laid over ground the mapper wants
smooth to walk on. Skipping it is correct. What a player sees standing there is a prop — a rock, a
crate — sitting on top of it. Skip the tool material, never draw the props, and the hole is exactly
prop-shaped.

**Why:** the diagnostic instrument was a coverage grid built from faces, so it could report "these
cells have no drawn face" and could rank the filters that might have dropped one. It could not
report that the missing thing was never a face. Every hypothesis it produced was about the
candidates it could see.

**How to apply:** when a rendered picture has a hole, ask what CLASS of geometry could occupy it
before asking which filter dropped it. Region-shaped failure means geometry; material-shaped means
shading — and "no geometry of a kind you parse" is a third answer neither question reaches. The
owner named it from memory of playing the map, which beat the measurement; on a game map, ask what
is actually there. Related: [[fixtures-are-the-weak-point]],
[[measure-the-output-not-the-capability]], [[fallbacks-do-not-make-guesses-safe]].

