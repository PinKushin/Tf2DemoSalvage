---
name: never-cancel-a-running-review
description: Never cancel a model's running review or trial; the owner's free-account limits end runs, not the lead. Paid OpenCode trials may run if the account has them.
metadata:
  type: feedback
---

Owner, 2026-11-20, after the lead cancelled Gemini 3.1 Pro and Kimi K3 trial reviews on hearing *"the models have to be
free"*: *"never cancel a model in an instance like this, the trials, if i have them can be used, but i probably wont have
them, because im on a free account. so i have token cutoffs and stuff."*

**Why:** the account's own limits stop what it cannot pay for; a cancel throws away a result the account may have
been able to give.

**How to apply:**
- Never `task_cancel` a running review or trial model. Let it finish or fail on its own (402, token cutoff).
- "Free" means: when choosing a NEW OpenCode reviewer, pick a free model first. A paid one already started stays running.
- Cancel only when the owner says so, or a task is a duplicate the lead started by mistake and has produced nothing.

Related: [[three-reviews-every-merge]], [[never-stop-a-working-subagent]].
