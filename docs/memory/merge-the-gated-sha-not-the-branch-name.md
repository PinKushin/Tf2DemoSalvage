---
name: merge-the-gated-sha-not-the-branch-name
description: A worktree on a detached HEAD leaves the branch ref behind; merging the branch name then lands a stale commit. Check the ref equals the gated SHA first.
metadata:
  type: feedback
---

2026-11-16, B759: the worktree had committed seven commits on a DETACHED HEAD. The branch ref
`fix/b759-item-panel-coverage` stayed at the first commit. `git merge --no-ff <branch name>` in the main checkout
merged that one commit (56fb496dc, "3 files changed"), not the gated head `09b18a0e5`. It was caught only because the
merge's stat looked too small. A second merge (d1427987d) brought in the rest.

**Why:** the gate and the reviews ran on the worktree's HEAD, but `git merge` resolves the NAME.

**How to apply:**
- Before merging, `git rev-parse <branch>` must equal the SHA the gate ran on (the `review-done` SHA). If it does not,
  `git branch -f <branch> <sha>` after `git merge-base --is-ancestor`.
- After merging, `git diff --quiet HEAD <gated sha> -- <paths>`.
- `git branch --show-current` printing nothing in a worktree means detached. Branch before the first commit.

Related: [[check-the-worktree-before-every-git-command]].
