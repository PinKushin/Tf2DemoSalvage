---
name: parent-branch-defers-the-gate
description: "When fixes chain off each other, collect them on a parent branch and gate once when the parent goes to main, not per fix."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-25T19:51:44.868Z
---

When one fix leads straight into the next, collect them on a parent branch. Branch each fix from the
parent, merge back with no gate. Run the three-phase gate once, when the parent merges to main.

**Why:** owner, when a gate was about to run mid-chain: *"no gate i dont wwant to wait for that jesus
christ thats why i said to create a parent branch, if you didnt want to go to main without the gate."*
The gate protects main; running it per chain step just makes the owner wait.

**How to apply:** if a branch's name no longer fits the work, rename to the parent. Keep
branch-per-fix under the parent. Gate only on the merge to main. See
[[gate-once-per-merge-not-per-commit]].
