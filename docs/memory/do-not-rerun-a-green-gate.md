---
name: do-not-rerun-a-green-gate
description: Re-running the gate after changing only floors or docs measures nothing and costs minutes the owner is waiting through.
metadata:
  type: feedback
---

Owner, 2026-08-24: *"and you just ran the damn gate, there was no reason to run it again"* — said
while waiting to go to bed, after a green gate was followed by edits to `build/gate.sh` floors and
`docs/RISKS.md`, and then another full gate run.

**Why:** a floor is compared against a count the previous run already produced, and a docs edit
cannot change a test result. The second run could only reproduce the first. The gate is minutes long
and it was time the owner spent waiting.

**How to apply:** after a green gate, ask what changed. Test code, production code or project files
mean run it again. Floors in `gate.sh`, `docs/`, `docs/memory/` or a commit message do not — take
the counts from the run already in hand and commit. Related: [[read-the-trx-total-not-the-console]],
[[push-when-the-gate-is-green]].
