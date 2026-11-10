---
name: avoid-commands-that-prompt
description: Prefer the command form that needs no approval prompt; a prompt can sit unseen for hours and stalls the whole session.
metadata:
  type: feedback
---

Owner, 2026-11-09: *"those prompts have sat up for hours without me noticing"* — about `git branch -D` after each merge,
which `~/.claude/hooks/confirm-destructive-git.ps1` asks about.

**Why:** an approval prompt blocks the session until the owner happens to look, often hours; the work stalls with
nothing to show for it.

**How to apply:** pick the form a hook does not ask about when it does the same job. After a merge to `origin/main`,
delete the branch with `git branch -d` (refuses an unmerged branch; warns but deletes one merged upstream), never `-D`.
Remove a clean worktree with `git worktree remove` (refuses uncommitted changes). If only a prompting form will do,
say so in the reply rather than leaving it waiting. Related: [[branch-granularity-is-fine-here]].
