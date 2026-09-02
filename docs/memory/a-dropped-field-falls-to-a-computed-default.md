---
name: a-dropped-field-falls-to-a-computed-default
description: What a dropped value falls to is decided by the transforms downstream of it, not by the field's own range — so predicting the symptom from the wire is wrong.
metadata:
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-02T00:00:00.000Z
---

**Before predicting what a missing field looks like on screen, follow the value through every
transform between the decode and the draw.** The failure mode is set by the LAST step, not the
first, and the two can disagree completely.

B269, and the wrong prediction was written into three files before it was measured:

- `m_flPoseParameter` is sent **normalised 0..1** (`baseanimating.cpp:243`). A sentry's `aim_yaw`
  runs −180..180. So a dropped value is 0, which is −180 degrees: barrel swung fully round. That
  reasoning is correct about the wire and wrong about this program.
- `EntityModelSet.Filled` leaves an uncomputed parameter at a **raw** zero and normalises it
  afterwards. Zero over a symmetric range normalises to **0.5** — dead centre. Every sentry drew
  level and pointing straight ahead.

**The correction matters more than the arithmetic, because the two predictions have opposite
consequences for whether the bug can survive.** A barrel at −180 is a bug report filed by the first
person who saw it. A barrel pointing forwards is a sentry. The second one lives for years, and it
lived here.

So the general rule: **a plausible default is what hides a dropped field, and whether the default is
plausible depends on code you have not read yet.** `Body`, `Skin`, `PlaybackRate` and `RenderMode`
all had this shape — see [[output-level-assertion-or-it-is-not-done]] and
[[sentinels-conflate-unknown-with-answer]] — and in every case the value the field fell to was
legal, so nothing could report it.

Two practical consequences:

- **Measure the symptom before writing it down.** One probe run said 0.5, not 0. The prediction had
  already reached a conformance test's remarks, a fixture comment and a probe's comment.
- **Choose fixture ranges that separate the cases.** A pose parameter over 0..100 cannot tell
  "arrived as 50" from "never arrived"; over −50..50 it can, because the missing answer is the
  midpoint and any real value is not.
