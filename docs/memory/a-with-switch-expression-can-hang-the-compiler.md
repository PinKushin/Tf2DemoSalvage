---
name: a-with-switch-expression-can-hang-the-compiler
description: "A big switch expression of nested `with` expressions hung csc on Tf2DemoSalvage.Animation for 7+ minutes; a build stuck on one project is the code"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-24T00:36:28.188Z
---

A ~20-arm switch expression, each arm a nested `with` (`staged with { Sounds = staged.Sounds with {
… } }`), made `csc.exe` on Tf2DemoSalvage.Animation run 7+ minutes — the same logic as a switch
STATEMENT over locals built in 8 seconds.

**Why:** 2026-09-23, looked like a deadlock between two test runs; three rounds of killing processes
before `timeout 300 dotnet build <project> -p:UseSharedCompilation=false` showed csc itself never
finished.

**How to apply:** when a build stalls on one project right after an edit, suspect the edit before the
machine — build that project alone under `timeout`, shared compilation off. Write long key dispatch
as a switch statement. Related: [[my-own-processes-are-mine]].
