---
name: ui-suite-optional-until-ui-grows
description: On this project, don't gate non-UI changes on the UI suite yet — it's ~8 tests covering nothing recent. That flips once the UI grows.
metadata:
  type: feedback
---

Owner, 2026-08-18: not worried about running the Viewer3D UI suite on this project **yet**. It is
only about eight tests, and recent work has not added anything a UI test checks — no new UI tests
have been built lately, so re-running the existing ones on a docs or decode change proves nothing.

**Why:** the value of a UI suite is catching regressions in UI you have, and this project's UI is
still tiny. Running it (under `run-exclusive.ps1`, taking the desktop) costs more than it returns for
a change that cannot touch the viewer.

**How to apply:** for a change that does not touch viewer/UI code — decode, docs, corpus tests,
Core/Content — `bash build/gate.sh` (the six non-UI assemblies) is enough; skip the UI phase. Still
run it when the change touches the viewer, or when asked. **This qualifies, for this project's
current phase only, the standing rule in [[ui-tests-run-every-time]]** — that rule flips back on the
moment the UI grows: the owner was explicit that "UI tests become very important once we start adding
more to the UI." New UI still ships with its own UI test.

**Restated more sharply on 2026-08-24, and it is stronger than "skip it when irrelevant":** *"the ui
tests dont gate us yet, we simply dont have enough of them to have it as a gate, we basically check
nothing with ui tests"*. So the suite does not block a merge AT ALL right now, even for viewer
changes — run it, read it, fix what it finds, but do not treat red there as a stop.

Note `CLAUDE.md` still describes the gate as two phases with the UI suite inside
`run-exclusive.ps1`. That is how to RUN it, not a statement that it gates.

**A worked example of why 14 tests check little, from the same day.**
`Click_TheCycleTargetButton_ReachesTheSpectatorCode` counted the log line `"following entity N"` to
prove a click reached `CycleTarget`. But that line is written only when the target search SUCCEEDS;
the other branch writes `"nobody else to follow at this tick"`. Both prove the wiring. So the test
asserted "the click reached the handler" by requiring "and it found somebody" — a fact about the
demo and the tick. Once B171 required a target to be alive and drawn, the solo POV era specimen the
UI session opens legitimately produced no target, and the test went red against a viewer the owner
was watching work correctly. His verdict: *"that seems like a stupid test for a pov demo or a demo
with a single player, it doesnt actually check anything"*.

Fixed by counting the `[spectate]` area instead of either message — which also sharpened the
negative control, since in the free camera `CycleTarget` returns before logging anything, so the
count proves the handler never RAN rather than merely that it found nobody.
