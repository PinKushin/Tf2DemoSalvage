---
name: lcor-is-not-in-a-worktree
description: "tools/corpus/local is git-ignored, so a worktree has none; pass lcor demos by the main checkout's absolute path"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 7256e9d8-efff-49d6-8602-f5a3d8ea7240
  modified: 2026-09-22T18:23:21.684Z
---

`tools/corpus/local/` (lcor) is git-ignored, so a worktree under `.claude/worktrees/` has no copy. A
relative lcor path from a worktree opens the viewer with no demo, and the run looks like it worked.

Use the main checkout's absolute path:
`C:/Users/pinku/source/repos/PinKushin/Tf2DemoSalvage/tools/corpus/local/<demo>`.

**Why:** a 90-second f12 measurement ran with no demo loaded (2026-09-22); the owner noticed on
screen.

**How to apply:** before any viewer/probe run from a worktree, check the demo path exists. Related:
[[the-f12-demo-is-the-parity-reference]].
