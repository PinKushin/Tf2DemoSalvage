---
name: review-for-recall
description: "Merge reviews favour recall over precision; several independent passes on large diffs, each finding verified before fixing"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-15T02:31:34.317Z
---

Owner, 2026-10-14: *"id rather false psitives then misses"*.

Measured the same day on feat/timeline-builder (3d3f5457), same prompt: Fable reviewer 145k, Opus reviewer 124k, Sonnet reviewer 119k (3 low-confidence false alarms), lead's `/code-review high`. All four found the obvious defect; EACH found something no other did. The worst bug (resume of a later checkpoint after an earlier one loses elements) only `/code-review` found. Sonnet is not the cheap reviewer — same reading cost as Opus.

**How to apply:** large or risky merge = `/code-review high` by the lead + one reviewer agent (fable or opus, `caveman:cavecrew-reviewer`). The implementer verifies each finding before fixing. Small-merge rule pending round 2 (volume diff). Related: [[read-the-diff-before-merging-agent-work]], [[never-use-fable]].
