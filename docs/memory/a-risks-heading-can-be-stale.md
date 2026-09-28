---
name: a-risks-heading-can-be-stale
description: "An OPEN heading in docs/RISKS.md is not evidence the work is open; check git log, the probe list and the code before proposing it"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-24T20:12:26.048Z
---

On 2026-09-24 three RISKS entries were proposed as open work; all three were already done: B396 (heal
beam), B316 (corpse pose), B306 (contact manifold). Owner on the last: *"wait thats done, we spent all
last week doing that"*. Nobody had updated the headings after the work landed.

**Why:** RISKS is appended to far more than its headings are edited — an old OPEN heading looks
exactly like live work.

**How to apply:** before proposing/starting a RISKS entry, spend one minute checking: `git log
--oneline -i --grep=<topic or B-number>`, the probe list, a symbol search for what's called missing,
the entry's own later dated updates (often further down than the first screen). If done, close the
heading with a pointer to the commits. See [[filing-a-divergence-is-not-fixing-it]],
[[one-place-or-it-drifts]].
