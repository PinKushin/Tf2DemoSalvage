---
name: read-the-diff-before-merging-agent-work
description: "The lead reads an agent branch's non-test diff before merging; a report plus green gates is not a review"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-14T19:16:16.084Z
---

Before merging an agent's branch, read `git diff main...<branch>` (at least the non-test files). For a large change, also run a reviewer agent (`caveman:cavecrew-reviewer`).

**Why:** owner, 2026-10-14: subagents cost more tokens than inline work, "but it also gives me a reviewer before any merge". That day the lead had merged four agent branches on the agent's report and green gates alone, which catches neither a wrong design nor silent scope creep.

**How to apply:** every agent merge. Report what the diff showed, not only the agent's summary. Related: [[review-subagents-deeply]].
