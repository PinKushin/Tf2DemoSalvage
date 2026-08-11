# Stryker notes

JSON has no comments and **Stryker rejects unknown keys outright** — a `_comment` field in
`stryker-config.json` is not ignored, it aborts the run with a list of allowed keys. That is why
this is a separate file. (Found the hard way: a comment key was added to the core config,
validated as JSON, and committed without running Stryker.)

## Path globs are project-relative

Every path glob in these configs resolves against **the mutated project's own directory** —
`managed/Tf2DemoSalvage.Core` or `managed/Tf2DemoSalvage.Cli` — not the solution root and not
the directory `dotnet stryker` was invoked from.

A glob written from the repo root matches nothing, and Stryker does not warn: it runs and reports
a score for whatever it did find. Same failure shape as `actions/upload-artifact` finding no
files — a clean-looking result for a step that did nothing.

```jsonc
"mutate": ["Schema/**/*.cs"]                              // correct
"mutate": ["managed/Tf2DemoSalvage.Core/Schema/**/*.cs"]  // matches nothing, reports success
```

Neither config sets `mutate` today, so this has not bitten yet.

**Unverified, and worth checking on the next run:** the core config's
`since.ignore-changes-in` lists `**/docs/**` and `**/tools/**`. Those are outside the mutated
project, so if the globs are project-relative they may never match — meaning a documentation-only
commit would not be ignored and `--since:main` would re-mutate everything. That costs runtime
rather than correctness, and looks identical to a normal run. Confirm by making a docs-only
commit and checking whether Stryker reports zero changed files.

## Expect survivors in `Program.cs`

The CLI's `Program.cs` is argument dispatch and file I/O. The behaviour worth pinning was
deliberately extracted into `CommandLine` and `ProgressBar` so it could be tested without running
the tool, so mutants that survive in `Program.cs` are usually mutants of the plumbing that
remains. Read the survivors rather than the score — see `docs/DECISIONS.md` D15.

## `ignore-methods` on the CLI, and why it is set that way

`stryker-config.json` for the CLI sets `"ignore-methods": ["WriteUsage", "WriteLine"]`.

**`WriteUsage` is there for documentation and does nothing** — `ignore-methods` matches method
*calls*, not declarations, so naming the method whose body you want skipped has no effect. It is
kept only so the intent is visible next to the entry that does work.

**`WriteLine` is the one that works, and it is deliberately broad.** Without it, 28 of the CLI's
mutants were help text: `writer.WriteLine("  -t, --trace  ...")` mutated to `""` or deleted.
Killing those means asserting every line of usage prose, which is a change-detector test — it
fails whenever the help is reworded and detects no defect. Score went 73.9% to 97.5% on that
entry alone.

The cost is honest and worth stating: it also removes real `WriteLine` mutants from the
denominator, such as deleting the `wrote N commands` line. That one is still *asserted* by
`ProgramTests`, it simply no longer counts. The trade is 28 worthless assertions against a
handful of real mutants that tests already cover.

## Known equivalent mutant

`ProgressBar._last = string.Empty` survives mutation to any other string. The field is only ever
compared against a rendered bar, and no rendered bar equals a Stryker sentinel, so no test can
distinguish the two. Equivalent by construction — do not write a test for it.

An earlier survivor, `bar.Finish()` in `Program.Run`, was removed rather than tested: the bar is
now scoped in a `using` so disposal provides the same ordering, and the unobservable statement is
gone.


## `test-runner` must be `mtp`, and getting it wrong looks like bad tests

xUnit v3 test projects are self-executing and run on Microsoft.Testing.Platform. Stryker's
default runner is VsTest, and against a v3 project it **runs to completion, reports
`Errors: 0`, and scores almost everything as survived**:

```
VsTest (default)   Killed: 1    Survived: 78   score  1.27 %
mtp                Killed: 76   Survived:  1   score 98.73 %
```

Same code, same tests, same Stryker 4.16.0. The 1.27% is not a measurement — the tests never ran
against the mutants, so every one of them "survived" by default.

**The reason this is worth a section rather than a config line:** the failure presents as a
quality problem, not a tooling problem. A 1.27% score with zero errors reads as "the test suite
is worthless" and invites someone to go write assertions that already exist. Nothing in the
output says the runner failed to invoke anything.

The same shape as the other two traps recorded here — a non-matching `mutate` glob, and a
comment key that aborts the run. Stryker fails quietly in more than one way, so **treat any
sudden score collapse as a tooling question before a test-quality one.**

## A glob guard that does not work, and one that does

The obvious check that `mutate` globs match the intended files — *"NoCoverage should be near
zero"* — **cannot detect a wrong glob.** A file excluded by a glob generates no mutants at all,
so it never appears in any status bucket. NoCoverage stays zero precisely because the file was
skipped.

Use instead: **assert the set of files in the report equals the expected set.** Stryker lists
only files it actually mutated, so a missing entry names the offending glob directly.

| | symptom |
|---|---|
| every glob wrong | zero mutants; Stryker cannot calculate a score — loud |
| one glob wrong | a real run, a plausible percentage, a fraction of the intended set — silent |

The D25 split was expected to need about six globs — six chances to write one repo-relative out of
habit — and in the end it uses **none**. Each project instead names its `project` and its
`test-projects` and mutates all of that assembly. That avoids the silent failure above and buys
the one below instead, which is at least loud in the report.

## A per-project score is not a quality measure, and 80 was not reachable

Three test projects, two of them (`Core.Tests`, `Corpus.Tests`) pointed at the same
`Tf2DemoSalvage.Core.csproj`. Stryker mutates the whole assembly for each, so **every run scores
all of Core against only that project's tests.** Code exercised solely by the corpus is
`NoCoverage` in the core run, and vice versa; neither number describes the suite.

First core-only measurement, on `mutation-box` 2026-08-10 at 539389d, 33m05s:

```
Killed: 798   Survived: 147   Timeout: 10   NoCoverage: 100   CompileError: 691
final mutation score 76.59 %
```

76.59% is `(798+10)/(798+10+147+100)`, confirming D24's formula. The CompileError mutants are
excluded from the score entirely and are not a finding.

**The 100 NoCoverage are the split artefact, and they cost about eight points.** Score them out
and the same run is `808/955` = **84.6%**, above the 80 floor. So core's tests are not the
problem; being asked to answer for corpus-only code is.

That makes `break: 80` a gate this project cannot pass however good its tests get, and it fired on
the first scheduled-style run. Set to `break: 0` for now, matching `Corpus.Tests` — but the better
fix is a config whose `test-projects` lists **both** Core.Tests and Corpus.Tests, which would move
those 100 into a real bucket and put the floor back in play. Unmeasured cost against an already
hours-long corpus run, hence not done yet. **`Cli.Tests` keeps `break: 80`**: it is the only
project whose tests cover the whole of what it mutates.

## A truncated run prints a full report with a plausible score

Thirty minutes before the run above, the same project on the same box reported **37.74% in
11m16s** — and it was not a measurement. Both runs' per-file rows are byte-identical for as far
as run 1 got (`DemoCommandReader` 12/0/63, `DemoHeader` 10/0/7, `ChatMessage` 85.29), then run 1
stops. The totals give it away:

| | killed | timeout | survived | nocov | accounted |
|---|---|---|---|---|---|
| truncated | 113 | 7 | 98 | 100 | **1215** |
| complete | 798 | 10 | 147 | 100 | **1954** |

739 mutants were never tested, and nothing in the output says so: Stryker printed *"All mutants
have been tested, and your mutation score has been calculated"* and a percentage that is
internally consistent with the subset it had. The likely cause is an interrupted run — the
replacement started 31 seconds later — but the cause is not the lesson.

**Compare the accounted total against a known-good run before believing a score.** This is the
same rule as reading a test runner's `Total:` rather than its `Passed!`, and it caught a real
error here: the 37.74% was reported upward as a genuine first measurement and three conclusions
were drawn from it, including a GitHub job timeout sized against the wrong number.

## Timeouts are scored as kills

`(Killed + Timeout) / (Killed + Timeout + Survived + NoCoverage)` — verified against a real
report in `DECISIONS.md` D24. A timeout is not evidence a test caught anything, so always read
the floor (timeouts counted against) alongside the headline. Three files in this repo have
reported a perfect 100% while most of it was timeouts.

`additional-timeout` is therefore a knob that moves the score in both directions. Raise it only
to learn what a set of timeouts resolves to, and lower it again afterwards.

## Coverage capture fails, and it is the reason a run takes 18 hours

Measured 2026-08-11, from the `mutation-box` corpus run and two local runs.

The box run finished in **18 h 07 m** and reported **100.00 %**. That score is an artefact:

| | |
|---|---|
| Killed | 183 |
| Survived | 0 |
| **Timeout** | **1142** |
| CompileError | 1081 |

**Stryker counts a timeout as a kill**, so 86 % of the "kills" are mutants that never returned a
verdict. 1142 x ~57 s is essentially the entire wall clock.

The cause is one line, printed 18 hours before the run ended:

```
[00:04:17 INF] Coverage capture complete: 0 mutations covered, 0 static mutations
[00:04:17 ERR] It looks like the test coverage capture failed. Disable coverage based optimisation.
```

With no coverage data Stryker cannot tell which tests touch a mutant, so it runs **the whole
suite for every mutant**. Against the corpus project — integration tests over real demo files —
that exceeds `additional-timeout` constantly.

**It is not unbounded loops in the parser.** That was the first hypothesis and it does not
survive the report: the largest timeout cluster is 100 mutants in `DemoTextDumper.cs`, and lines
54-56 there are `ArgumentNullException.ThrowIfNull` calls mutated to `;`. Removing a null check
cannot hang. A few mutants genuinely can spin — `Snappy.cs` `read++` to `read--` is a real
infinite loop — but they are a rounding error against 1142.

**Switching `test-runner` to `vstest` does not fix it, and fails worse.** Tried locally: coverage
capture still fails, and the symptom inverts — instead of running everything and timing out,
mutants come back `Survived` without a verdict. A scoped run reported `Killed: 0, Survived: 95`
against 731 passing tests, which is not a result. Reverted; `mtp` is demonstrably less broken
because it at least produced 183 real kills.

**Open, and the thing to fix before booking any recurring slot.** Until coverage capture works,
no mutation number from this repo means anything, in either direction.

Two things to try, neither verified:

1. Pin a Stryker version known to capture coverage under .NET 10 (SDK here is 10.0.302).
   Coverage collection is the part most likely to break on a new SDK.
2. Mutate against `Tf2DemoSalvage.Core.Tests` rather than the corpus project regardless.
   731 fast unit tests are a better mutation harness than 78 integration tests over 20 MB of
   demos, and the corpus project should be a separate, rarer run.

## `string.Create(CultureInfo.InvariantCulture, $"...")` cannot be mutated

The 1081 compile errors are almost all one shape:

```
CS1620: Argument 2 must be passed with the 'ref' keyword
```

Stryker rewrites the interpolated string into a conditional, and the interpolated-string handler
is a `ref struct` passed by ref, so the rewritten form does not compile. This codebase uses that
idiom for every culture-safe message, so a large fraction of mutants are dead on arrival. Not a
defect to fix in the source — worth knowing so the created-versus-tested gap is not read as
something else.
