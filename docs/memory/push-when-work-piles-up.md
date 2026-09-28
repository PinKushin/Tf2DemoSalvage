---
name: push-when-work-piles-up
description: Push a branch before a large amount of work sits on one machine — crash safety outranks "push sparingly"; commit a building, green state first, never one with a sabotage applied.
metadata:
  type: feedback
---

Owner, seeing ~10,000 unpushed lines on a long-running branch: *"make sure to push so we dont lose a
massive amount of work if the computer crashes or windows goes tits up."*

**Why:** the standing rule is push sparingly (a push costs CI). A multi-day port piles commits onto
one disk, and a crash loses all of it — that costs more than any CI run.

**How to apply:** when a branch carries a large amount of unpushed/uncommitted work, commit a state
that builds and passes and push, without waiting for a logical unit to finish. **Never commit while a
sabotage run has its edits applied** — wait for it to restore first. Pushing a feature branch is not
merging: the gate still runs once before merge ([[gate-once-per-merge-not-per-commit]]).
