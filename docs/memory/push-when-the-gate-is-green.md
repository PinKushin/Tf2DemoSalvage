---
name: push-when-the-gate-is-green
description: On this project, push after every green gate rather than batching local commits.
metadata:
  type: feedback
---

**Push when the gate is green.** Owner, 2026-08-23: *"we need to push when the gate is green too"*.

**This overrides the global "push sparingly" default**, which says to hold local commits until a
logical unit is finished. Here the gate passing *is* the signal — a green gate means the work is in
a shareable state, so it goes up.

**How to apply:** after `bash build/gate.sh` reports every project at or above its floor, commit and
push. Run the UI suite first when the change could touch the window, since the gate deliberately
excludes it.

**Do not gate for the sake of it.** Same conversation, on running the gate after writing up a
finding: *"you realize if you just found an issue and havent done anything to fix it, you dont have
to run the gate before the commit, nothing has changed"*. The gate answers "did I break the code";
a documentation-only change has not. Batch the gate with the code it guards.

Related: [[a-floor-must-track-the-number-it-guards]] and [[read-the-trx-total-not-the-console]] for
reading the gate's output correctly.
