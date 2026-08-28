---
name: an-empty-box-must-never-cull
description: "A zero-sized bounding box degenerates any spatial test into a point test at the object's origin, which answers a question about the origin rather than about the object."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-28T04:59:48.872Z
---

Before culling, occluding, sorting or bucketing by a bounding box, check the box has **volume**. A
zero-sized box is not "a very small object" — transformed, it collapses to a single **point at the
matrix's translation**, and every spatial test then answers a question about that point.

Measured 2026-08-28. `BrushModels` built its `ModelFrames` with no render bounds, so every brush
entity — door, lift, gate, payload cart — carried the default box. A submodel is compiled about its
own origin, so the point sat at the **map centre**, and doors popped in and out of view as the map
origin drifted through the frustum. The owner saw a roller door on badlands flickering between its
grate and the concrete wall behind it.

Two fixes and both are needed. Supply the real bounds — `dmodel_t` carries mins and maxs, and the
reader already kept them. And guard the cull: an object whose box has no volume is **drawn**, never
tested.

**Why:** this is the conservative rule the rest of the project already applies — never cull what
cannot be proved invisible — and the one place it was not applied is the one place it was needed.
The failure is silent and intermittent, which is the worst combination: it looks like a rendering
glitch or a z-fight rather than a missing input. See [[a-null-object-default-hides-a-missed-wiring]].

**How to apply:** when adding a spatial optimisation, enumerate every source of the bound it reads
and check each one actually supplies it — a `default` struct is a legal value that no test will
reject. Then write the pair of tests: an object with no bounds survives, and an object WITH bounds in
the same place does not. The second is what stops the first being satisfied by a cull that never
culls.
