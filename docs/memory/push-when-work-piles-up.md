---
name: push-when-work-piles-up
description: Push a branch before a large amount of work sits on one machine — crash safety outranks "push sparingly"; commit a building, green state first, never one with a sabotage applied.
metadata:
  type: feedback
---

**The owner, 2026-09-13, seeing about ten thousand unpushed lines on `fix/b369-ivp-narrow-phase`:** *"make sure to
push so we dont lose a massive amount of work if the computer crashes or windows goes tits up"*.

**Why:** the standing rule is to push sparingly, because a push costs a CI run and bandwidth. A long port — B369 runs
for days — piles commits and uncommitted files onto one disk, and a crash loses all of it. Losing the work costs more
than any CI run.

**How to apply:** when a branch carries a large amount of unpushed or uncommitted work, commit a state that builds and
passes, and push the branch, without waiting for a logical unit to finish. **Never commit while a sabotage run has its
edits applied** — wait for it to restore, then commit. Pushing a feature branch is not merging: the gate still runs once
before the merge ([[gate-once-per-merge-not-per-commit]]).
