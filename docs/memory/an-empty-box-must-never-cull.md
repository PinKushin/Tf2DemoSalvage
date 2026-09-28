---
name: an-empty-box-must-never-cull
description: "A zero-sized bounding box degenerates any spatial test into a point test at the object's origin, which answers a question about the origin rather than about the object."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-28T04:59:48.872Z
---

Before culling/occluding/sorting/bucketing by a bounding box, check it has **volume** — a zero-sized
box transformed collapses to a point at the matrix's translation, so every spatial test then answers
a question about that point.

Measured 2026-08-28: `BrushModels` built `ModelFrames` with no render bounds, so every brush entity
(door, lift, gate, cart) carried the default box. A submodel compiles about its own origin, so the
point sat at the map centre — doors popped in and out as the map origin drifted through the frustum
(a roller door on badlands flickering between grate and wall).

Fix needed both halves: supply the real bounds (`dmodel_t` carries mins/maxs, already read but
unused), and guard the cull — an object whose box has no volume is **drawn**, never tested.

**Why:** this is the conservative rule the project already applies elsewhere (never cull what can't
be proved invisible) — silently missing in the one place it was needed. Silent and intermittent, the
worst combination — looks like a rendering glitch, not a missing input. See [[logs-are-the-debugger]].

**How to apply:** when adding a spatial optimisation, enumerate every source of the bound it reads and
check each actually supplies it — a `default` struct is a legal value nothing rejects. Write the
pair of tests: an object with no bounds survives, and one WITH bounds in the same place does not.
