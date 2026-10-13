---
name: log-findings-to-the-measurement-box-log
description: "When a measurement check or CI/mutation/fuzz review finds something, file it in PinKushin/MEASUREMENT-BOX-LOG.md so working agents can act on it"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 51df669e-fe15-45c0-8fd6-ea15807f434b
  modified: 2026-10-12T17:09:12.266Z
---

When a measurement check (scheduled or ad hoc) finds a failed or cancelled mutation/fuzz run, a score drop, or a crash input, write an entry at the top of `PinKushin/MEASUREMENT-BOX-LOG.md` (the log's own Conventions apply: tag, `date` clock time, quoted output, run ids).

**Why:** owner, 2026-10-12: "its a pita to tell the working ai's to fix stuff without you keeping notes". The log is how working agents learn what to fix; a finding left only in a notification or chat is lost to them.

**How to apply:** one entry per distinct finding, not re-filed for the same run ids; a clean run writes nothing. The scheduled task `tf2-measurement-check` (step 6 of its SKILL.md) does this automatically. The log is named for the subject, not a repo, and sits in no git repo.

See [[no-paid-compute-oracle-dropped]] — measurement runs are on GitHub Actions now, not the boxes.
