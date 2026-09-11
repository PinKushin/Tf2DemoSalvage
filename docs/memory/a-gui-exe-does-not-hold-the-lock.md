---
name: a-gui-exe-does-not-hold-the-lock
description: PowerShell's `&` returns at once for a GUI-subsystem exe, so run-exclusive releases the desktop lock while tf2demoview is still open; use Start-Process -Wait
metadata:
  type: feedback
---

**`& tf2demoview.exe ...` inside `pwsh -Command` does not wait.** tf2demoview is a GUI-subsystem
executable, and PowerShell's call operator returns immediately for those. Wrapped in
`run-exclusive.ps1 pwsh -Command "dotnet build ...; & ...tf2demoview.exe ..."`, the background task
reported "completed, exit 0" about forty seconds in — the build's time — while the viewer window stayed
open for the owner to look at. The machine-wide lock was already released, so any other agent could
have taken the desktop mid-inspection, which is exactly what the lock exists to prevent.

**Why:** the lock must span a person's manual check (global CLAUDE.md, "Exclusive workloads"). A
"completed" notification during a launch is the tell.

**How to apply:** when a launch builds first and so needs a `pwsh -Command` wrapper, start the viewer
with `Start-Process -Wait -FilePath <exe> -ArgumentList ...` (or pipe it to `Out-Null`), so the
wrapper — and therefore the lock — lives as long as the window. Git Bash invoking the exe directly
does wait, which is why the CLAUDE.md command-table form never showed it. Related:
[[take-your-own-screenshot]].
