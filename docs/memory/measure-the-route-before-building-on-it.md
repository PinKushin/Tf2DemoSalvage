---
name: measure-the-route-before-building-on-it
description: A planned data route is a guess until measured. "leaf → leaffaces → dispinfo" reaches none of cp_badlands' 1191 displacement faces, and one query said so before any code was written.
metadata:
  type: feedback
---

**Before building on a route through the data, measure that the route arrives.** One query, first,
costs minutes; discovering it from a symptom costs a rewrite.

**Why:** the displacement-collision plan's step 2 was *"leaf → `LUMP_LEAFFACES` → faces →
`dispinfo`"* — written from the format documentation and entirely reasonable. Measured on
`cp_badlands` it reaches **zero** of the 1191 displacement faces. A displacement's base quad is not
its terrain, so the compiler files it under no leaf at all; the narrowing has to be by BOUNDS.

Had the narrowing been built first, the symptom would have been "terrain collision does nothing",
which looks exactly like a wrong primitive — the expensive place to go looking.

**The measurement needs a control or it proves nothing about the format.** Zero displacement faces
reached is also what a wrong `dleaf_t` offset produces. The same walk reached **12,654 flat faces**,
and 13845 − 1191 = 12654 exactly, so every flat face is reachable and no displacement face is. That
is the format, not the reader. See [[an-empty-search-needs-a-control]].

**How to apply:**

- When a plan says "A names B", write the query that counts how many A actually name a B, and run it
  on real data before writing anything else.
- Include the negative class as the control — here, the faces that are NOT displacements.
- Keep the measurement as a test rather than deleting it. `LeafDisplacementReachTests` asserts the
  zero, so nobody re-attempts the route, and it says so the day a map does put them in leaves.
- **A published tool is a source when the engine's own file is not.** `cmodel_disp.cpp` is not in the
  SDK; `vrad` building its own displacement list rather than using leaves was the hint that leaves
  were never the route.

**The same session, from the other side:** two test premises were wrong about the MAP rather than
about the code — a box dropped 512 units onto a vertex at z = 288 stops at 793, because the map
stacks terrain above terrain; and the space just above a displacement vertex is usually inside the
brush the terrain was carved from, so a brush trace correctly reports startsolid. Both times the
code was right and the prediction was a guess about geometry nobody had looked at. **When a
prediction about real data fails, ask whether the data is what you assumed before touching the
code.** [[nothing-is-closed]] is the same rule for inputs.

Related: [[nothing-is-closed]], [[a-filed-design-choice-may-not-be-one]],
[[instrument-bugs-outnumber-decoder-bugs]].
