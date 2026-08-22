---
name: a-skip-is-not-a-pass-or-a-failure
description: A broken precondition disarms a test via Assert.Ignore — invisible in a summary line and it passes the count floor.
metadata:
  type: project
---

**A test whose precondition breaks does not fail. It skips — and a skip is invisible.**

Found for real on 2026-08-22. `BspModelsTests` looked up TF2's install path, found nothing because
its hardcoded copy of that path was corrupt ([[edit-files-with-the-file-tools]]), and took its
`Assert.Ignore` branch. The map had gone unread for an unknown length of time. Nothing anywhere
reported it.

**Why it survives every instrument this repo has:**

- The console prints `Passed!` with no mention of it.
- `build/assert-test-count.sh` reads the trx `total`, which **counts skipped tests**, so the floor
  is satisfied — see [[read-the-trx-total-not-the-console]].
- Coverage does not move enough to notice.
- The test is still there, still green, still named as if it measures something.

This is the shape of [[measure-the-output-not-the-capability]] one level up: the *fallback branch*
made a dead test look like a healthy one, exactly as a fallback in production makes a dead feature
look implemented.

**How to apply:**

- **A guard clause is a claim, so make it checkable.** The reason 72 other files can still hide this
  is that each states its own precondition. `GameInstall` (D52) is one copy, so everything that
  reads game data skips together — loudly and obviously — rather than one file skipping alone.
- **When a suite's skip count is non-zero, find out which tests and why.** 13 skips in Content.Tests
  is normal only once each one has been accounted for. An unexplained skip is a finding.
- **Prefer a helper that returns null-for-absent over a caller-written `File.Exists`.** The check is
  precisely where the silence gets in, so it belongs in one place that is itself tested.
- Suspect this first whenever a test "has always passed" but you cannot remember it ever producing
  output. Check `Skipped:` in the run before assuming the code path is covered.
