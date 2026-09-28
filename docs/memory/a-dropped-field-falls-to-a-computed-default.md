---
name: a-dropped-field-falls-to-a-computed-default
description: What a dropped value falls to is decided by the transforms downstream of it, not by the field's own range — so predicting the symptom from the wire is wrong.
metadata:
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-02T00:00:00.000Z
---

**Follow a missing value through every transform between decode and draw before predicting how it
looks on screen** — the failure mode is set by the LAST step, not the first, and the two can disagree
completely.

B269: `m_flPoseParameter` is sent normalised 0..1 (`baseanimating.cpp:243`); a sentry's `aim_yaw` runs
−180..180, so a dropped value (0) predicted as −180° — barrel fully swung round. Correct about the
wire, wrong about this program: `EntityModelSet.Filled` leaves an uncomputed parameter at raw zero
and normalises *afterwards*, so zero over a symmetric range becomes **0.5** — dead centre. Every
sentry drew level and pointing forward, not swung round.

**Why the correction matters more than the arithmetic:** a barrel at −180° is a bug report filed
immediately; a barrel pointing forward is a sentry, and that bug survives for years.

General rule: **a plausible default is what hides a dropped field, and plausibility depends on code
you haven't read yet.** `Body`, `Skin`, `PlaybackRate`, `RenderMode` all had this shape — see
[[output-level-assertion-or-it-is-not-done]], [[sentinels-conflate-unknown-with-answer]]. Practical
consequences: measure the symptom before writing it down (a wrong prediction had already reached a
test's remarks, a fixture comment, and a probe comment); choose fixture ranges that separate "arrived
as X" from "never arrived" (0..100 can't; −50..50 can, since missing = midpoint).
