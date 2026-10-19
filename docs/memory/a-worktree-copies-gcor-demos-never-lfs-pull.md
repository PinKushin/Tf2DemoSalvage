---
name: a-worktree-copies-gcor-demos-never-lfs-pull
description: "A worktree's gcor demos are LFS pointers; copy the real files from the main checkout, never git lfs pull (spends GitHub LFS bandwidth)."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-19T21:35:39.661Z
---

Owner, 2026-10-19: *"but i dont want them pulling i want them copying"*. A fresh worktree's `tools/corpus/demos/*.dem` (gcor, committed via Git LFS) are LFS pointers (~130 bytes); a test or probe then fails with "cannot open z1800.dem: invalid demo header ID".

**Fix:** remove that one pointer file in the worktree, then `cp -n` the real file from the main checkout's `tools/corpus/demos/`. `git status` stays clean (the LFS clean filter maps the content back to the same pointer). lcor is a separate junction (`cmd //c "mklink /J tools\corpus\local F:\Tf2DemoSalvage-lcor"`; remove it with `cmd //c rmdir`, never `rm`).
**Why:** GitHub's free LFS tier is 1 GiB/month and every pull spends it (D81).**How to apply:** put this in every agent brief whose tests or probes open a demo; a skipped test is not a pass. Related: [[lcor-is-not-in-a-worktree]], [[a-worktree-lcor-is-a-junction]].
