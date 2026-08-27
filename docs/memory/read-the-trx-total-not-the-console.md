---
name: read-the-trx-total-not-the-console
description: How to read a test run honestly — the trx total, not the console; a floor that tracks the suite; a skip is invisible; and a wrong invocation exits 0.
metadata:
  type: project
---

**Three memories were merged into this one on 2026-08-27** — `a-floor-must-track-the-number-it-guards`,
`a-skip-is-not-a-pass-or-a-failure` and `a-wrong-invocation-exits-zero`. They already cross-referenced
each other in every direction, because they are one subject: **"Passed!" is not the result, and every
number beside it can lie in a different way.**

## The trx total, not the console

**`build/assert-test-count.sh` reads `total=` out of the `.trx`. The console prints a different,
smaller number. Bump floors from the trx.**

Measured on Content.Tests, 2026-08-22, same run:

| source | number |
|---|---|
| console `Total:` | 623 (610 passed + 13 skipped) |
| trx `<Counters total=>` | **638** |
| trx `executed=` | 610 |

The gap is `[Explicit]` tests, which are discovered and counted in the trx total but not run. The
floors in `build/gate.sh` are therefore all trx numbers, and this is why the file's comments can say
things like "613: SoundFormatProbe, `[Explicit]`" — an `[Explicit]` probe raises the floor even
though it never executes.

**Why it is worth knowing:** reading the console after adding 9 tests gave 623 against a floor of
628, which looks exactly like *tests were silently lost* — the precise failure the floors exist to
catch. Several minutes went into hunting a regression that was not there. The real count was 638,
which is 628 + 9 conformance + 1 probe, matching to the unit.

```bash
bash build/assert-test-count.sh "**/content.trx" <old-floor> content
```

It prints `content: 638 executed, 0 failed (floor 628)` — that first number is the new floor. Run it
rather than doing arithmetic on the console line.

---

## `a-floor-must-track-the-number-it-guards`

**A floor that has not been raised is not a guard.** `build/assert-test-count.sh` exists precisely to
catch a truncated run, and its floors had drifted an order of magnitude behind the suites:

| Assembly | Real count | Floor |
|---|---|---|
| Viewer | 352 | **34** |
| Core | 1034 | 744 |
| Corpus | 138 | 99 |

A solution-wide run that reported **50 of Viewer's 350 tests** as a pass (B104) satisfied a floor of
34 without complaint. The check was present, ran, printed a reassuring line, and meant nothing.
Floors must be raised as the suite grows — [[mutation-score-is-not-the-goal]] is the same discipline
applied to a different number.

**Run one project at a time.** A solution-wide `dotnet test` writes one `.trx` per project all under
the same file name, so no count check can tell them apart afterwards; and it runs test assemblies
concurrently, which is the leading suspect for the truncation itself. `build/gate.sh` does the
sequential, count-asserted run — use it rather than reading console lines.

**`--filter` changes which tests EXIST, not merely which of them run.** NUnit's adapter includes
`[Explicit]` tests when no filter is given and drops them as soon as any filter is present.
Content.Tests reported **441 unfiltered and 436** with `--filter 'FullyQualifiedName!~UiTests'` — the
five being diagnostic probes. That filter was the documented merge gate for months, so every
`[Explicit]` test in the repository was quietly absent from it.

Two invocations that look equivalent can therefore report different totals for two unrelated reasons.

---

## `a-skip-is-not-a-pass-or-a-failure`

**A test whose precondition breaks does not fail. It skips — and a skip is invisible.**

Found for real on 2026-08-22. `BspModelsTests` looked up TF2's install path, found nothing because
its hardcoded copy of that path was corrupt ([[edit-files-with-the-file-tools]]), and took its
`Assert.Ignore` branch. The map had gone unread for an unknown length of time. Nothing anywhere
reported it.

**Why it survives every instrument this repo has:**

- The console prints `Passed!` with no mention of it.
- `build/assert-test-count.sh` reads the trx `total`, which **counts skipped tests**, so the floor
  is satisfied.
- Coverage does not move enough to notice.
- The test is still there, still green, still named as if it measures something.

This is the shape of [[measure-the-output-not-the-capability]] one level up: the *fallback branch*
made a dead test look like a healthy one, exactly as a fallback in production makes a dead feature
look implemented.

- **A guard clause is a claim, so make it checkable.** The reason dozens of files could hide this is
  that each stated its own precondition. `GameInstall` plus `Skip` (D52, D109) is one copy, so
  everything that reads game data skips together — loudly and obviously — rather than one file
  skipping alone. See [[extraction-without-adoption-is-not-dry]] for how long that took to finish.
- **When a suite's skip count is non-zero, find out which tests and why.** 13 skips in Content.Tests
  is normal only once each one has been accounted for. An unexplained skip is a finding.
- **Prefer a helper that returns null-for-absent over a caller-written `File.Exists`.** The check is
  precisely where the silence gets in, so it belongs in one place that is itself tested.
- Suspect this first whenever a test "has always passed" but you cannot remember it ever producing
  output. Check `Skipped:` in the run before assuming the code path is covered.

**The skip is still the right behaviour** — it is the only thing that keeps CI green on the machine
without the game ([[ci-is-the-machine-without-tf2]]). What is wrong is a skip nobody accounted for.

---

## `a-wrong-invocation-exits-zero`

**A command that was invoked wrongly usually exits 0.** Printing a usage banner is not an error to
the program that printed it, so "did it pass" and "did it run at all" collapse into the same answer.

Three measured on this project, all of which cost real time:

- **`pwsh run-exclusive.ps1 dotnet test …`** — the script lives at the PinKushin root, not in the
  repo, so `pwsh` cannot find the bare filename, prints its own usage banner and **exits 0**. It was
  in `CLAUDE.md` in that form for weeks. Correct: `pwsh -File "C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1" …`
- **`dotnet test … | tail`** — the pipeline's exit code is `tail`'s. A broken build came back exit 0
  with an empty grep and read as green. Redirect to a file, then check `$?`.
- **`dotnet test --filter` matching nothing** — exits 0 with no summary at all, so a renamed fixture
  silently tests nothing.

**The general shape: whenever a command's OUTPUT is being read rather than its exit code, the
absence of expected output is the failure signal, and nothing reports it.** So assert on the shape —
a total that matches a known floor, a line that must appear — rather than on the status.

This is the same family as `--no-build` and as a stale binary: the run succeeds at doing nothing.

---

Related: [[measure-the-output-not-the-capability]], [[do-not-rerun-a-green-gate]],
[[instrument-bugs-outnumber-decoder-bugs]], [[push-when-the-gate-is-green]].
