---
name: a-bug-branch-merges-when-the-bug-is-done
description: "Never merge a B-numbered branch with parts of the same bug filed open; fix them on the branch first, only owner-blocked parts may stay (D258, hook tf2-merge-closes-its-bug)"
metadata:
  type: feedback
---

2026-11-14: B734's sub-view sprites were gated and about to merge, with impacts, ropes and the no-Z order filed as "still
open" under the same B number. The owner said: *"fix those other bugs before the merge, i hate you leaving bugs open
before a merge, they should not merge until there is basically no bugs to do with it left"*.

**Why:** a slice merged with its sibling parts filed as open is a half-shipped feature. Each extra merge also costs a
review, a gate and a note. It is [[an-open-list-is-not-a-stopping-point]] applied to the merge, not only to the end of a
turn.

**How to apply:**
- When reading the engine turns up more divergences of the same bug, fix them on the SAME branch before merging. Filing
  them in RISKS is a to-do list, not permission to merge.
- Only parts that need the owner (a decision, a capture by eye) may stay open. The heading then says "needs owner".
- The hook `tf2-merge-closes-its-bug.ps1` refuses the merge while the branch's `## B<N>` heading says "open". Keep the
  heading honest: it is the line the hook reads.
