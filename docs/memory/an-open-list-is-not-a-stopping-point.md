---
name: an-open-list-is-not-a-stopping-point
description: "Never end a turn on a list of filed gaps that sit inside the task's scope — keep implementing until the list is empty or an item truly needs the owner; a release is a checkpoint, not an ending (D249)"
metadata:
  type: feedback
---

On 2026-11-08 a turn ended after beta.62, with finding 107's B702 gaps listed as "still open". The owner was angry. Their
words: *"why do you just leave shit open stop fix it"*, *"you just wasted over 10% of my weekly token budget by not
running"*, *"I TOLD YOU TO FINISH"*.

**Why:** the owner's weekly budget is a window, and an idle turn wastes it for good. The gaps were already inside the
authorized task ("keep going / get shadows working"). A clear list of what is left looks like a handoff, but it is work not
done. This is the same trap as [[filing-a-divergence-is-not-fixing-it]].

**How to apply:**
- After a release or a commit, take the next open item at once. Do not write a summary and stop.
- Stop only when one of these is true: the list is empty, an item needs an owner decision, an item needs something only the
  owner can give (a capture by eye, a file), or the owner says stop.
- When the owner says budget is fine, run readers, reviewers and sabotage checks in parallel while you implement inline
  ([[implement-inline-on-opus-or-fable]]).
- A status line between steps is fine. A final answer that lists remaining in-scope work is not.
