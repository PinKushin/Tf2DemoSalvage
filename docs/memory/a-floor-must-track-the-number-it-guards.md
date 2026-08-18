---
name: a-floor-must-track-the-number-it-guards
description: The test-count floor sat at 34 against a suite of 352, so a run reporting 50 passed it; and --filter silently drops every [Explicit] test.
metadata:
  type: project
---

**A floor that has not been raised is not a guard.** `build/assert-test-count.sh` exists precisely to
catch a truncated run, and its floors had drifted an order of magnitude behind the suites:

| Assembly | Real count | Floor |
|---|---|---|
| Viewer | 352 | **34** |
| Core | 1034 | 744 |
| Corpus | 138 | 99 |

A solution-wide run that reported **50 of Viewer's 350 tests** as a pass (B104) satisfied a floor of
34 without complaint. The check was present, ran, printed a reassuring line, and meant nothing.
Floors are now 340 / 1000 / 130 and must be raised as the suite grows —
[[mutation-score-is-a-ratchet]] is the same discipline applied to a different number.

**Run one project at a time.** A solution-wide `dotnet test` writes one `.trx` per project all under
the same file name, so no count check can tell them apart afterwards; and it runs test assemblies
concurrently, which is the leading suspect for the truncation itself. `build/gate.sh` does the
sequential, count-asserted run — use it rather than reading console lines.

**`--filter` changes which tests EXIST, not merely which of them run.** NUnit's adapter includes
`[Explicit]` tests when no filter is given and drops them as soon as any filter is present.
Content.Tests reports **441 unfiltered and 436** with `--filter 'FullyQualifiedName!~UiTests'` — the
five being diagnostic probes. That filter was the documented merge gate for months, so every
`[Explicit]` test in the repository was quietly absent from it.

Two invocations that look equivalent can therefore report different totals for two unrelated reasons.
See [[a-log-must-name-what-it-measured]] — an instrument that reports a true number about the wrong
population is worse than one that reports nothing.
