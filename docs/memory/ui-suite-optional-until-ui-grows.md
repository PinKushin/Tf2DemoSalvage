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
