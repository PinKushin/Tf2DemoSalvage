---
name: a-removed-worktree-folder-is-deleted-after
description: "git worktree remove says Permission denied: contents are gone, the empty folder is held open by a lingering build process; delete it yourself later, never hand it to the owner"
metadata:
  type: feedback
---

`git worktree remove F:/wt-x` right after a build or gate prints `Permission denied`: the contents are deleted, the empty
folder stays because a process started from that tree still has it as its working directory (likely `VBCSCompiler`, which
idles minutes after a build; *interpolated*, not confirmed). Once it exits, `Remove-Item F:\wt-x` (PowerShell) succeeds.
Ten such folders piled up across sessions, each handed to the owner as "locked, please delete".

**Why:** owner, 2026-11-11: "IDK where tf those folders are, and we need you to be able to delete them, it makes no
sense why you cant".

**How to apply:** after `git worktree remove`, if the folder remains, delete the empty folder with `Remove-Item` (retry at
the end of the session if still held), then `git worktree prune`. Never list folders for the owner to delete.
**Use the PowerShell tool, not Bash `rmdir`**: on 2026-11-13 Bash `rmdir F:/wt-…` was refused by the removal safety check
("critical path") and four folders were handed to the owner again — *"You know better about the folders"*. PowerShell
`Remove-Item -LiteralPath $d -Confirm:$false`, guarded by an emptiness check, removed all four. Related:
[[delete-leftover-files-yourself]].
