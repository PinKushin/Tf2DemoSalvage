---
name: gate-once-per-merge-not-per-commit
description: The full gate is ~15 minutes and guards a MERGE; iterate on the touched project's tests plus a solution build, and gate once before merging.
metadata:
  type: feedback
---

Owner, after one session ran the full gate five times in a few hours: *"so many fucking gate runs and
they take like 15 mins each fuckking time."*

**Why:** the gate walks twelve assemblies (~15 min) and exists to stop a broken tree being MERGED. A
commit only needs a clean build; running it per small change bought nothing a targeted test run
didn't, and each run owns the working tree, stalling other edits.

**How to apply:**
- While iterating: `dotnet test tests/<touched project>` + `dotnet build Tf2DemoSalvage.slnx`.
- Gate once, at branch end, before `git merge --no-ff` and push.
- "Gate green" only if a gate actually ran on it — say what did: project count + build.
- Exceptions wanting an early gate: a shared helper many assemblies consume, `build/gate.sh` itself,
  anything touching decode.
- Never stop a gate halfway — a stopped gate orphans its test tree; `pwsh build/reap-dotnet.ps1`
  after.
- **A fresh worktree lacks `celt.dll`/`speex.dll`** (gitignored native audio builds) — phase 1 dies at
  Audio. Owner, on the B404/B405 merge: *"if you didnt touch audio, then dont worry about audio run
  everything else."* Run the
  remaining assemblies by hand and say so, or `cp -n` the DLLs from the main checkout (no-clobber copy
  is exempted from the write hook — *"copy is nondestructive, so, is allowed as a script"*; never a
  bash `cp` for him, his terminal is PowerShell).
