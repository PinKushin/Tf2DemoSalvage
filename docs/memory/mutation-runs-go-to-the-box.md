---
name: mutation-runs-go-to-the-box
description: "A whole-file Stryker run goes to the mutation box, never local; local Stryker only when scoped to a few lines."
metadata:
  type: feedback
---

**The box is gone (D212, 2026-10-08): "the box" now means `.github/workflows/mutation.yml` (dispatch with `project=`).**

2026-10-03: a local `dotnet stryker --mutate **/EntityModels.cs` held the desktop lock for hours; the UI and playback
gates and another agent queued behind it. Owner: *"that run will take forever locally, unless its extreamly narrow
scoped, you verify that by waiting on a box run, or running it manuall on the box if nothing is running or would
conflict"*.

**How to apply:**
- Verify a mutation campaign on mutation-box: wait for the nightly run, or run it by hand when `flock -n
  /tmp/measurement-box.lock` is free and `crontab -l` shows nothing due.
- By hand: own `git worktree add ~/tmp-<name> <branch>`, remote tmux, under the flock, log to a file.
- Local Stryker only for a few lines or one method (it takes the desktop lock).
- Put this in every mutation agent's prompt.

Related: [[mutation-score-is-not-the-goal]], [[one-run-at-a-time]].
