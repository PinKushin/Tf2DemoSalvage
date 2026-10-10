---
name: steerable-work-is-done-inline
description: "Work where the owner's TF2 knowledge or eye matters is done in the main session, not a subagent; pure engine ports stay with opus agents."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-10T02:32:31.843Z
---

Owner, 2026-10-09: he cannot message subagents or comfortably read their transcripts ("the half window bugs me"),
so steering goes through the lead: "with opus, you might just want to take over."

**Split:** main session does anything steerable - live TF2 client, the golden-comparison instrument, visual
judgement, "does this look right" (B161, B514). Opus agents keep pure engine ports (SDK read -> code -> tests,
e.g. B513). Sonnet keeps docs and bounded jobs.

**Why:** an unsteered agent needed three rounds on B514 and missed `demoui` for the tick; the owner would have
said it in seconds had he seen the reasoning.

**How to apply:** the owner compacts often to keep the main session cheap - suggest it at task boundaries (right
after a merge), never mid-task. Never stop a running agent to take over; switch at its report.
Related: [[agents-ask-the-owner-client-questions]], [[one-subagent-and-prefer-cheap-models]].
