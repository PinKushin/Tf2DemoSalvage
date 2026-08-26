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

**Sub-branch pushes are crash insurance, and the owner asked for them explicitly** (2026-08-26):

> *"push too, i dont mind pushes on subbranches, expecially when they get over 1k lines, because
> losing this much work due to a crash would suck."*

**The one thing to watch is CI volume**, also owner-stated: *"the only think to watch for, when
pushing, is too many ci's running."* Measured against the workflows rather than guessed:

| workflow | triggers | cost of a sub-branch push |
|---|---|---|
| `test.yml` | `push: [main]`, `pull_request` | **none** |
| `codeql.yml` | `push: [main]`, `pull_request` | **none** |
| `fuzz.yml`, `mutation.yml` | schedule + dispatch | none |

**So a sub-branch push costs nothing at all** — nothing triggers on it. Confirmed 2026-08-26: one
push to `main` produced exactly two runs (Test and CodeQL) and a sub-branch push produced none. That
is what makes "push freely on sub-branches" free rather than merely tolerated.

**The trap is opening a PR.** `pull_request` has **no branch filter**, so a PR flips that branch
from zero cost to a full Test + CodeQL run on *every* push to it — and `test.yml`'s corpus job pulls
Git LFS blobs against a 1 GiB/month tier. Do not open a PR on work in progress unless CI feedback is
actually wanted.

**Pushes to `main` are the expensive ones**, so batch those. `concurrency: cancel-in-progress`
supersedes an in-flight run for the same ref, so rapid pushes do not stack — but each one restarts
the LFS pull, which is the budget that actually binds.

Related: [[a-floor-must-track-the-number-it-guards]] and [[read-the-trx-total-not-the-console]] for
reading the gate's output correctly.
