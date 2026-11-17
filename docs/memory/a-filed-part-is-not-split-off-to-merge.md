---
name: a-filed-part-is-not-split-off-to-merge
description: A part named in a bug's own RISKS entry is finished on that branch, even when its mechanism differs; never propose a new B number to merge the rest (D261).
metadata:
  type: feedback
---

B754 named four parts, the last the war paint retexture (material compositor). I proposed filing the retexture under a new
B number and merging the rest. Owner, 2026-11-16: *"Keep it unmerge and finish it"*. Second time: B761 (*"Those need tests before
the merge and push though"*).

**Why:** D258, a bug branch merges when the bug is done; a different mechanism does not make it a different bug.

**How to apply:** finish every part the entry lists on its branch. Propose a split only for something the entry never
named. Related: [[a-bug-branch-merges-when-the-bug-is-done]], [[a-coverage-gap-found-is-tested-before-merge]].
