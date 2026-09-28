---
name: a-gui-exe-does-not-hold-the-lock
description: "PowerShell's `&` returns at once for a GUI-subsystem exe, so run-exclusive releases the desktop lock while tf2demoview is still open; use Start-Process -Wait"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-11T01:31:49.622Z
---

**`& tf2demoview.exe ...` inside `pwsh -Command` does not wait** — it's a GUI-subsystem exe, and
PowerShell's call operator returns immediately for those. Wrapped in `run-exclusive.ps1 pwsh -Command
"dotnet build ...; & ...tf2demoview.exe ..."`, the task reported "completed, exit 0" ~40s in (the
build's time) while the viewer window stayed open. The lock was already released mid-inspection.

**Why:** the lock must span a person's manual check (global CLAUDE.md, "Exclusive workloads"). A
"completed" notification during a launch is the tell.

**How to apply:** when a wrapper builds first, start the viewer with `Start-Process -Wait -FilePath
<exe> -ArgumentList ...` so the wrapper (and lock) lives as long as the window. Git Bash invoking the
exe directly does wait. Related: [[take-your-own-screenshot]].
