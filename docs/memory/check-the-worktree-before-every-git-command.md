---
name: check-the-worktree-before-every-git-command
description: "An agent's worktree can be removed under it and its cwd moved into another agent's; check pwd and git rev-parse --show-toplevel before every build, run, commit or git command."
metadata:
  type: feedback
---

2026-10-17: an agent's own worktree was auto-removed mid-session and its working directory silently became the
coordinator's worktree. It then created a branch, built, ran the viewer and committed there — two commits landed on the
coordinator's merge branch while that merge's UI suite was running. Coordinator: *"Before every build, run, commit or git
command, check `pwd` and `git rev-parse --show-toplevel` and stop if they are not"* the agent's own worktree.

**How to apply:**
- Prefix every build, test, viewer run and git command with `git rev-parse --show-toplevel` and compare it.
- Short worktree paths (e.g. `F:\hud-wc`); deep ones fail on Windows.
- A script that launches a binary refuses an exe outside its own worktree.
- A demo recorded with TF2's `record` lands in the game's `tf/`; copy it to lcor and have it removed — the game folder
  keeps nothing of ours.

Related: [[a-worktree-lcor-is-a-junction]], [[parallel-agents-lock-the-gate-too]].
