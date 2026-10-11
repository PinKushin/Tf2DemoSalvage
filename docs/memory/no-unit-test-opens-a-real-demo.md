---
name: no-unit-test-opens-a-real-demo
description: Unit test projects never open a real match demo; build a synthetic one or a short authored specimen (D213)
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-11T15:21:38.265Z
---

No test in a unit test project (Core, Scene, Content, Animation, Audio, Presentation, Rendering, Viewer3D) opens a
real recorded demo — "at least not a full fledged 30+min long demo". Build the state with `SyntheticDemo` or the
entity directly, or use a short authored specimen.

**Why:** owner, 2026-10-11: *"its breaking my rules using a real demo anyway, those are suppose to be unit tests
are they not? no unit test should ever use a real demo, at least not a full fledged 30+min long demo"*. Found
when the Rendering suite's gate run took 18.6 minutes: its real-demo face and trail tests cost 30-114 s each
(one demo and map loaded per test, no shared fixture), and the same tests are what made mutation runs unfinishable
([[mutation-score-is-not-the-goal]]). It restates D38 ([[fixtures-are-the-weak-point]],
[[author-the-specimen-the-corpus-lacks]]) for a place it had been let slip.

**How to apply:** when writing or briefing a test, a real demo is allowed only in `Corpus.Tests` (and probes).
The "output-level assertion on real data" rule ([[output-level-assertion-or-it-is-not-done]]) is met there or
with a real MAP/model, not by loading a match demo into a unit suite. Say this in every implementer brief.
My interpretation, not the owner's words: a real map or model file is still allowed in unit suites; he named demos.
