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

## A superset gate in a worktree needs lcor joined to the main checkout's (2026-09-30)

`build/gate.sh` without `GCOR_ONLY` finds lcor as the sibling `tools/corpus/local` of the gcor directory it
walks up to. A worktree whose `local` holds a few copied demos runs gcor PLUS THOSE — reported as a superset,
green, and blind: B440's first "superset" read 2 lcor demos of 49 and never met the two it fixed.

**How to apply:** before a superset in a worktree, `ls tools/corpus/local | wc -l` (the main pool is 49); if
short, move the copies aside and `cmd //c "mklink /J tools\corpus\local <main checkout>\tools\corpus\local"`.

**And pass `TF2DEMOSALVAGE_GCOR_ONLY=0`: `gate.sh` defaults it to 1** (line 63). CLAUDE.md said "`bash build/gate.sh`
(no `GCOR_ONLY`)" for the superset until 2026-09-30; every "superset" run that way was gcor. The tell: `corpus: 181
executed` is gcor's count; the full pool executes ~199.

