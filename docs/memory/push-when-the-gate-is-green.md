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

**A push to `main` is not finished until its run is read.** Measured 2026-08-26: two consecutive
merges to `main` left the Test job RED and it went unnoticed for over an hour, because pushing felt
like the end of the task. A green local gate says nothing about CI — that is the entire point of
having CI — and this project's standing rule already goes further than a status tick (*"CI
annotations count as build output"*). Not looking at all is worse than trusting the tick.

**Both failures were things only CI could see, which is why the local gate stayed green:**

- **Floor drift.** `build/gate.sh` and `.github/workflows/test.yml` carry the same numbers in two
  places, and lowering one without the other fails only in CI. It happened twice in one session
  (presentation and viewer). `CLAUDE.md` warns about this in those words, so the fix is mechanical:
  after changing any floor, diff the two lists and confirm they agree.
- **An environment-dependent test.** `WiringUiTests` asserted the viewer's log never says "no player
  appearance" — true on a machine with TF2 installed, false on a runner without it, where the
  appearance legitimately cannot build. **A test that passes locally and fails in CI is usually a
  test asserting on the developer's machine**, and the fix is the assembly's existing
  `ViewerSession.RequireTheGame()` gate rather than weakening the assertion.

**A run can be ABSENT, and absent looks exactly like "not looked at yet".** Measured 2026-08-26: a
merge pushed to `main` at 15:14 UTC produced **no workflow run at all**. Fifteen minutes later
`gh run list --branch main` still showed the previous commit's run at the top — green, and about a
different commit. Reading that list casually says "main is green".

Everything checked out: `origin/main` and `gh api repos/{repo}/commits/main` both held the merge,
all four workflows reported `state=active`, Actions permissions were `enabled`. The repository's
own event list had no `PushEvent` for `refs/heads/main` at that time either, though it did list a
sub-branch push twenty minutes earlier. A `workflow_dispatch` on the same ref queued immediately, so
nothing was broken — the push just did not dispatch.

**So verify by SHA, never by reading the top of the list:**

```bash
gh api "repos/{owner}/{repo}/actions/runs?head_sha=$(git rev-parse main)" --jq .total_count
```

`0` means no run exists; re-trigger with `gh workflow run <name> --ref main` rather than waiting.
Every workflow that gates a merge should therefore carry `workflow_dispatch`, or there is no way to
ask for the run that did not happen.

**And use the FULL sha.** The first attempt at that query padded a short sha out to forty characters
by hand; it returned `0` for a commit that does not exist, which is the same answer as the real
problem and would have been believed. `$(git rev-parse main)` rather than anything typed.

**How to apply:** after `git push origin main`, run the count query above, then
`gh run list --branch main --limit 3`. If a run is still in flight, come back to it — do not report
the work as landed until the run is read.

Related: [[a-floor-must-track-the-number-it-guards]] and [[read-the-trx-total-not-the-console]] for
reading the gate's output correctly.
