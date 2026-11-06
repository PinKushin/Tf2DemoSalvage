---
name: review-before-gate
description: Order is review, fix, then ONE gate — never start the gate alongside or before the review
metadata:
  type: feedback
---

The order of a change's last steps is: unit tests green, **review, apply the review's fixes, then run the gate once**, then commit
and push. Never start the gate in parallel with the review or before it.

**Why:** owner, 2026-11-06, after the gate was started beside a review for the third time and then had to be re-run on the
review's fixes: *"wait to run the gate until after you get the review, jesus christ"*. A gate started before the review is almost
always wasted (the review finds something, the code changes, the gate must run again), it costs ~15 minutes of desktop lock,
and editing source while it runs invalidates it anyway (twice this session).

**How to apply:** after the targeted tests pass, launch the reviewer (foreground) and wait for it. Fix its findings, rerun the
targeted tests, and only then start gate phase 1 and phase 2. No source edits while the gate runs. Related:
[[gate-once-per-merge-not-per-commit]], [[gate-once-per-batch]].
