---
name: a-coverage-gap-found-is-tested-before-merge
description: An untested branch found while closing a bug gets its test on that branch before merge and push; never split it off as a follow-up B-number.
metadata:
  node_type: memory
  type: feedback
  originSessionId: a7745234-ed21-42a5-a0c8-7c85003d2f62
  modified: 2026-11-16T17:01:39.532Z
---

Owner, 2026-11-16, when B759 (item panel coverage) was about to merge with the label placement and the inventory image's
other branches split off as B761: *"Those need tests before the merge and push though"*.

**Why:** a gap found during the work is part of that work; filing it as a new OPEN entry and merging leaves the branch
"done" with known holes, the same thing D258 forbids for a bug's own parts.

**How to apply:** when a review or the work itself finds an untested path in the code a branch covers, write the test on
that branch, before the gate. Split off only what is genuinely a different subsystem, and say why. Related:
[[a-bug-branch-merges-when-the-bug-is-done]], [[an-open-list-is-not-a-stopping-point]].
