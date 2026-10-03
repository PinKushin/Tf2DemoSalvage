---
name: full-corpus-at-most-once-a-day
description: "The full lcor superset gate runs at most once a day; a failure in it is rerun as that one test, never as the whole corpus."
metadata:
  type: feedback
---

2026-10-03: an agent re-ran the whole corpus (GCOR_ONLY=0) to recheck one failing lcor test, lost the output and started
again. Owner: *"if a 5+ hour run failed forget it"*, *"full corpus runs are like a once a day thing at most, they take
too long"*.

**Why:** the superset takes hours. Rerunning it for one test spends the day on a question one filtered run answers.

**How to apply:**
- Full superset at most once a day, for a decode change. Never per commit, never per agent.
- A superset failure is rechecked with `--filter` on that test (GCOR_ONLY=0) plus gate phase 1 (gcor). Not the corpus again.
- Agents never run the full corpus unless told.
- Run any long job with output to a log file, so the result survives the session closing.

Related: [[gate-once-per-merge-not-per-commit]], [[lcor-is-not-in-a-worktree]].
