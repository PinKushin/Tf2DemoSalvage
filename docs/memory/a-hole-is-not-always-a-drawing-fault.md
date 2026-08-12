---
name: a-hole-is-not-always-a-drawing-fault
description: "Black patches in a rendered map came from geometry never read, not from shading or culling; a face-based instrument cannot report a missing non-face."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-12T23:46:59.771Z
---

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
is actually there. Related: [[real-data-hides-bugs-small-inputs-expose]],
[[measure-the-output-not-the-capability]], [[fallbacks-do-not-make-guesses-safe]].
