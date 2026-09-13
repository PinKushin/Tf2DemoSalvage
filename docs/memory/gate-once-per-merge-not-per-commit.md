---
name: gate-once-per-merge-not-per-commit
description: The full gate is ~15 minutes and guards a MERGE; iterate on the touched project's tests plus a solution build, and gate once before merging.
metadata:
  type: feedback
---

The owner, 2026-09-12, after one session ran the full gate phase 1 five times in a few hours: *"so many
fucking gate runs and they take like 15 mins each fuckking time"*.

**Why:** the gate walks twelve assemblies and takes about fifteen minutes. It exists to stop a broken
tree being MERGED. That session ran it after each small change — a constants class, a one-line
tolerance, one interpolation function, a script wiring — every one of which touched a single
assembly. A commit only needs a clean build; the extra runs bought nothing a targeted test run did not.
And each run owns the working tree while it runs ([[read-the-trx-total-not-the-console]]), so source
edits stalled behind it and the waits compounded.

**How to apply:**

- **While iterating:** `dotnet test tests/<the project the change touched>` and
  `MSBUILDDISABLENODEREUSE=1 dotnet build Tf2DemoSalvage.slnx` for zero warnings. Seconds to a couple of
  minutes. Count the total against the known suite size as always.
- **Gate once, at the end of a branch, before `git merge --no-ff` and push.** Not per commit, not per
  feature step inside a branch.
- **A commit message may say "gate green" only if a gate actually ran on it** — which it usually will
  not, now. Say what did run: the project's count and the build.
- **Exceptions that do want an early gate:** a change to a SHARED helper many assemblies consume, a
  change to `build/gate.sh` itself, or anything touching decode. Say which exception applies.
- **Never stop a gate halfway to start another.** A stopped gate orphans its `dotnet test` tree; if a
  gate must be restarted, `pwsh build/reap-dotnet.ps1` after.
- **A fresh worktree has no `celt.dll` or `speex.dll`** (gitignored builds in `tools/native-audio/`), so
  phase 1 dies at Audio with `DllNotFoundException`. The owner, 2026-09-12, on the B404/B405 merge: *"if
  you didnt touch audio, then dont worry about audio run everything else you did change and merge"*. So
  when the branch does not touch Audio, run the remaining phase-1 assemblies against their gate floors by
  hand, skip Audio, and say so in the report. Do not hand him a bash `cp` to fetch the DLLs: his terminal
  is PowerShell, where it fails, and the file-write hook refuses the copy from the assistant's shell.
