---
name: a-lookup-is-not-a-loader
description: MapAssets.Geometry answers from a dictionary built at load; a model nothing else asked for never exists.
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T03:55:08.111Z
---

`MapAssets.Geometry(path)` looks like an on-demand model loader and is a `TryGetValue` over a
dictionary filled once, during `MapAssets.Load`, from the demo's model list plus the map's brush
entities. Anything else asks and gets null for ever (B363: the map's detail models).

**Why it matters:** the failure is silent and one layer away from where it looks. The model packs to
an empty entry, the placement code is correct, every count reads right, and the only symptom is the
renderer's *"was posed before its geometry was uploaded"* — which names the model but not the cause.

**How to apply:** a new population of models must be added to the list `MapAssets.Load` reads, next
to the brush entities, not merely requested later. Two more traps sit behind it, both now fixed and
both general: `EntityModelSet.Add` remembers a failed load as an empty entry permanently (right per
frame, wrong for a deliberate `Precache`, which now retries empty entries), and `MomentScene` used
to upload only when its OWN `Add` returned true, so a set grown anywhere else never reached the
device — `EntityModelSet.Grown` now says so however it grew.

See [[instrument-bugs-outnumber-decoder-bugs]] and
[[output-level-assertion-or-it-is-not-done]].
