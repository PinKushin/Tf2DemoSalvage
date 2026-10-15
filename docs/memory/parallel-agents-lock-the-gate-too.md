---
name: parallel-agents-lock-the-gate-too
description: "With several agents running, phase 1 and big probes also go through run-exclusive, or they starve another agent's UI suite"
metadata:
  node_type: memory
  type: project
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-14T22:46:07.760Z
---

2026-10-14, four agents at once: unlocked phase-1 gates (testhosts of ~3.4 GB) and TF2 map loads ran during other agents' UI suites. Failures: the 8x playback test short of samples, a UIA timeout, "Insufficient system resources" opening %LOCALAPPDATA%. All passed when rerun on a quiet machine.

**Rule while more than one agent runs:** `TF2DEMOSALVAGE_GCOR_ONLY=1 pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 bash build/gate.sh`, and every Probe run (enforced: ~/.claude/hooks/require-exclusive-lock.ps1). From the PowerShell tool, `bash` is WSL's (`system32\bash.exe`, no dotnet/pwsh) and the gate dies at core with "dotnet: command not found" — name `"C:/Program Files/Git/bin/bash.exe"` explicitly there (hit by three agents 2026-10-15). The lead clears any rerun; a starved failure is reported, never silently retried. TF2 and Ghidra are separate leases (~/.claude/hooks/lease-shared-mcp.ps1). Related: [[one-run-at-a-time]], [[tf2-is-a-shared-fixture]].
