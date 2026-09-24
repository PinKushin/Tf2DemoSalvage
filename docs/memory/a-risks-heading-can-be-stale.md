---
name: a-risks-heading-can-be-stale
description: "An OPEN heading in docs/RISKS.md is not evidence the work is open; check git log, the probe list and the code before proposing it"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-24T20:12:26.048Z
---

On 2026-09-24 I proposed three RISKS entries as open work, and all three had already been done: B396 (heal beam), B316
(corpse pose) and B306 (contact manifold). The owner corrected the last one: *"wait thats done, we spent all last week
doing that"*. Nobody had updated the headings after the work landed. The evidence was in plain view, including the probe
list naming `vphysics-pair-mindists`.

**Why:** RISKS is appended to far more often than its headings are edited. An old OPEN heading looks exactly like live
work, and proposing it wastes the owner's time and makes him correct history he has already lived through.

**How to apply:** before proposing or starting a RISKS entry, spend one minute checking whether it has already been done:
- `git log --oneline -i --grep=<topic or B-number>`
- the probe list (`dotnet run --project tools/Tf2DemoSalvage.Probe -c Release --`)
- a symbol search for what the entry says is missing
- the entry's own later dated updates, which are often further down than the first screen

If it is done, close the heading with a pointer to the commits. See [[filing-a-divergence-is-not-fixing-it]] and
[[one-place-or-it-drifts]].
