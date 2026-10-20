---
name: memory-keeps-lessons-not-superseded-copies
description: "Memory keeps wrong turns, TF2-client knowledge and history; a memory fully superseded by a decision or finding may be deleted. The 12 process-rule triggers stay."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-19T21:53:13.014Z
---

Owner, 2026-10-19, on compacting memory: *"maybe delete some of the old ones that have been superseded instead of keeping them. i know we had the rule of keeping stuff, but that was more so we dont go back down already known bad paths, and keep all the information we have on the tf2 client, and history."*

**KEEP:** a wrong turn and what killed it, anything about how the TF2 client or Valve's engine behaves, the history of his corrections and decisions (his words and why), process rules in force.
**DELETE / MERGE:** a memory fully superseded by a later memory, a numbered decision or a finding that carries all of it; a duplicate; a one-off state that no longer exists. Verify the owner carries everything before deleting.
**The 12 process rules** that a numbered decision restates (`american-english-for-new-text`, `keep-packages-current`, `ci-gates-are-soft-for-now`, `no-known-protocol-list`, `a-demo-without-its-map-fails-soft` and seven more, listed in `docs/memory-audit.md`) STAY: the index line is their only always-loaded trigger. Owner: *"the 12 we will keep i think"*, then *"yes, its practically settled, its not like we cant remove them later if we want"*.
**How to apply:** audit before deleting (`docs/memory-audit.md` is the worked example); memory lives in two copies (the repo's `docs/memory/` and the auto-memory directory), and a delete in the auto copy is permanent (no git history), so move owner-profile files to `~/.claude/memory/` instead of deleting. Related: [[one-place-or-it-drifts]].
