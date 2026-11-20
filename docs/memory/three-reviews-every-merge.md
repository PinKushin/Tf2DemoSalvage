---
name: three-reviews-every-merge
description: Every merge gets three independent reviews (Sonnet subagent, Grok, Fable) then the lead's integration review; every fix is re-reviewed the same way, including "did the fix add anything" (D263).
metadata:
  type: feedback
---

Owner, 2026-11-19, after Grok and Fable each found what a Sonnet review of B69 missed (3 and 6 findings, no overlap):
*"do those 3 reviews every time then, and make sure to run reviews on the fixes and make sure nothing has been added"*;
then *"or should we keep yours seperate so 4 reviews lol"*, confirming four.

**Why:** three readers found disjoint sets on one diff; one review misses most of what is there. The lead briefed the
work, so it is the least independent reader and is not one of the three.

**How to apply (D263):**
- Before the gate: Sonnet `bounded-task` (Agent tool), Grok (`delegate_task`, provider `grok`, model `grok-4.7`),
  Fable (`delegate_task`, provider `claudeAgent`, model `claude-fable-5-2`; the Agent tool's hook refuses Fable).
- Each later reviewer gets the earlier findings and is told to skip them.
- Then the lead: integration pass - the whole fits, conflicting findings settled, each fix did exactly its finding and
  nothing more.
- Fixes for findings: all four again on the fix diff, asked whether the fix added anything (behaviour, scope, a new
  defect). Repeat until a round finds nothing new; then gate, merge.

Related: [[review-before-gate]], [[review-for-recall]], [[grok-reviews-and-docs]], [[never-use-fable]], [[read-the-diff-before-merging-agent-work]].
