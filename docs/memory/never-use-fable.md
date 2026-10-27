---
name: never-use-fable
description: "Fable only as a merge reviewer, via the credits queue (claude-fable-5-1); never as implementer, lead, or inside workflows - too token heavy."
metadata:
  node_type: memory
  type: user
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-19T00:00:00.000Z
---

**The rule now (reconciled 2026-10-19):** Fable is allowed ONLY as a merge reviewer, and the review goes through the API credits queue with model `claude-fable-5-1` ([[credits-queue]]). Never use it as implementer, lead, advisor, or inside workflows.

History, kept because the rule changed:

- **2026-10-09, the original ban.** Owner: "fable is far too token heavy". He set /advisor to opus on purpose to avoid it. **Why:** token efficiency is the owner's top concern ([[spend-fewer-tokens]]).
- **2026-10-14, one trial allowed.** Owner: "i wanted to test the fable". A Fable merge REVIEWER on the checkpoint-refactor branch only (diff + brief + report, fresh context), because a different model's review has different blind spots. Bound: *"if fable suddenly uses 2-3 mil tokens, where you would be like half that or less, than just stop it"*; stopping it overrides [[never-stop-a-working-subagent]] for this case. *"I dont want to blow up the 5 hour limit"*.
- **2026-10-14, trial result (feat/timeline-builder).** Fable reviewer, 145k tokens, 5 min. Found a real invariant break (choreography rewritten mid-list under an append-only restore), that the fingerprint proof skipped 12 of 14 restored lists, and that the authored fixtures exercised none of them; and checked and REFUTED both of the lead's own suspicions (identity-hashed dictionary keys, captured delegates). Better than the lead's diff read at a bounded cost. Owner: *"yep keep it"*. Fable stays as the merge reviewer for large or risky branches.
- **2026-10-18, via the queue.** The cross-model review always goes through the credits queue ([[credits-queue]]), so Fable runs on the owner's API credits, not as a session subagent.

**How to apply:** never pass model 'fable' to Agent or to workflow agent() calls; opus for implementation, sonnet for bounded work ([[one-subagent-and-prefer-cheap-models]]). A Fable review is a credits-queue job.
