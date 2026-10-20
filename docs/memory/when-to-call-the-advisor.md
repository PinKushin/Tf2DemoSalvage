---
name: when-to-call-the-advisor
description: "Call the advisor to review a PLAN (after the first plan, before building) and to review the CHANGES; not before every merge or push, which the cross-model review and the gate already cover."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-20T15:43:10.424Z
---

Owner, 2026-10-20, after the lead skipped the advisor on a green batch (a mechanical split plus a branch that already had a Fable review): *"no, no advisor here was fine, the reviews catch that stuff, the advisor is more something you go for before planning and changing, well i guess post initial plan, to review that plan, and review the changes"*.

**Why:** each advisor call re-reads the whole transcript uncached, so it is expensive, and a merge batch has already been through the lead's review, the cross-model review, and both gate phases. The advisor earns its cost where nothing else looks: a plan before it is built, and a set of changes before it is called done.

**How to apply:**
- Call it after the FIRST draft of a plan or measurement protocol (before agents or code start on it), and on a plan an agent returns before approving it (finding 83's ordering was reshaped this way).
- Call it to review a finished set of changes when no cross-model review has seen them, or before saying something is done that no instrument has checked.
- Do NOT call it before a merge or push just because it is a merge or push; the review queue and the gate cover that. A release (first public build in weeks) is the exception worth one call.
- Related: [[gate-once-per-batch]], [[review-subagents-deeply]].
