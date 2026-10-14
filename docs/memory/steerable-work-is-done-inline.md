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

**Diagnosing a UI test is steerable (2026-10-13).** Not all UI work: running the suite as a gate and writing new
UI tests go on unattended, agent or lead, owner absent. DIAGNOSING a failing or odd UI test is inline, because
what happened on screen is the evidence and only the owner sees it. Owner: "if we are diagnosing a UItest, then
i need input a lot of the time, because YOU DONT HAVE EYES". Case: an opus agent moving the suite off real input
hit a full-screen/readout failure; the owner made six on-screen observations the lead relayed, one misread
("the sub knows more of whats going on than you do, this probably should have been done by you so i could help
more"). Switch point: when an agent's UI task turns into a diagnosis, take that part inline at its next report.

**How to apply:** the owner compacts often to keep the main session cheap - suggest it at task boundaries (right
after a merge), never mid-task. Never stop a running agent to take over; switch at its report.
Related: [[agents-ask-the-owner-client-questions]], [[one-subagent-and-prefer-cheap-models]].
