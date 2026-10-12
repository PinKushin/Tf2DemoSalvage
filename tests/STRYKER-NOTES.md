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

### What it cost `Viewer3D`, and what stops it (2026-10-12, D214)

The paragraph above says "not a defect to fix in the source", and for one mutant that is right. It is wrong about
the method: **Safe Mode removes every mutant in a method whose mutated form fails to compile**, so one
`string.Create` in a log line took the whole method out of the measurement. `Viewer3D` in CI: 4,355 mutants, 2,153
compile errors, 8.47%.

**Two shapes, and the mutators behind them** (*measured*, a local run's compile phase, which reports every method
whatever `--mutate` says):

| error | idiom | mutators |
|---|---|---|
| CS1620 | an interpolated string handed to a handler — `string.Create( CultureInfo.InvariantCulture, $"…" )`, `StringBuilder.Append( CultureInfo.InvariantCulture, $"…" )` | **String** rewrites `$"…"` as `( active ? $"" : $"…" )`, and a conditional cannot bind to a `ref` handler; the Conditional and Equality mutants INSIDE the holes ride in the same expression |
| CS0165, CS0170 | a guard that declares what the method then uses — `if ( x is not { } name ) return;`, `!map.TryGetValue( k, out var v ) \|\| …` | **Block removal** and **Statement** empty the exit, **Logical** turns `\|\|` into `&&`, **Negate** flips the test: each leaves the variable unassigned below |

**Config cannot stop it** (B410's measurement, not repeated here): `ignore-mutations` filters after the mutants are
compiled in. The comment is the only thing that prevents injection, and one line does it:

```csharp
// Stryker disable once all : <why> - B410
_renderLog.LogInformation(
    "{Message}",
    string.Create(CultureInfo.InvariantCulture, $"…"));
```

**`disable once all` before a statement covers the whole statement, however many lines it runs to** (*measured* on
`ForegroundProbe.Describe`, a three-line `return string.Create(…)`: 15 compile errors and a Safe Mode removal
before, none after, 7 mutants ignored with the comment's reason). That corrects an earlier note here that a
multi-line statement needs the `disable all` / `restore all` pair. For an expression-bodied member the comment goes
before the member. It costs the mutants inside that one statement, which for a log line or an early exit is the
intent.

**Done for `Viewer3D`**: 114 statements in `MainForm.cs`, one each in `ViewerMenu.cs` and `ForegroundProbe.cs`.
Found by listing the idioms and then by rerunning the compile phase until it named nothing. Before and after, the
whole project's mutants as one local run classes them:

| | compile error | Safe Mode removals | ignored by a comment |
|---|---|---|---|
| before | 2,197 of 4,400 | 75, over 40 methods | 0 |
| after | 529 | 0 | 750 |

The 529 that remain are single mutants that do not compile on their own (`Count()` to `Sum()`, an ordering
comparison on an enum) and take nothing else with them.

**`--mutate "File.cs{a..b}"` is a span of CHARACTERS, not lines** (*measured*: `{28..56}` on an 84-line file filtered
out every mutant on lines 31 to 74). The local hook's "at most 80" is therefore 80 characters; a method is covered by
consecutive spans, and a mutant wider than one span — a block removal — is left out.

## `MutationHeavy`: the tests a mutation run leaves out (2026-10-11)

**Stryker reruns every test that covers a mutant, once per mutant.** A test that takes seconds and
covers a whole reader is therefore paid thousands of times, and a handful of them decide whether a
run finishes at all.

*Measured.* The scheduled CI run was green daily through 2026-09-29 and cancelled at its ceiling
every day from 2026-09-30: Core finished 6,909 mutants in 54 minutes, then could not finish 7,031 in
90, after one 3.3-second test (`PropsAt_ManyThreadsSharingOneTimeline_MatchATimelinePerThread`,
B438) began covering most of `DemoTimeline`. The whole rest of Core's suite is 4 seconds. The first
local run of Content was stopped after seven and a half hours.

| project | suite, summed | methods over 1 s | their share |
|---|---|---|---|
| Core | 26 s | 3 | 5 s |
| Content | 502 s | 95 | 439 s |
| Rendering | 1,381 s | 123 | 1,350 s |
| Viewer3D | 150 s | 11 | 133 s |
| Presentation | 22 s | 1 | 21 s |

Scene and Animation have none; Audio has 20 at 1-2 s and runs in six minutes untagged.

**The mechanism.** Those methods carry
`[Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]`, and each of the five
`stryker-config.json` files has `"test-case-filter": "TestCategory!=MutationHeavy"`. Nothing else
filters on it: `dotnet test`, the gate and its floors run every test as before.

**The trade, stated.** A mutant only a tagged test would have killed is reported as surviving. That
is a true statement about the fast suite, and the remedy is the project's own rule (D38): write the
synthetic test that kills it rather than leaning on a real map.

**Keeping the list honest.** A new test over about a second, measured on the owner's machine,
gets the tag. To re-derive: `dotnet test <project> --logger "trx;LogFileName=timing.trx"`, sum
durations per method, tag what exceeds one second. A mutation run that stops fitting its ceiling is
the signal that one was missed - look for the newest slow test before raising a timeout.

**Rendering, re-derived 2026-10-11 after D213** (`docs/DECISIONS.md`): the suite is about 1,000 s
summed where it was 1,381 s (968 s and 1,060 s in two runs at three workers). Six tags came off and
thirty-nine went on, leaving 146 methods tagged.

**From the UNION of several runs, and that is now the procedure for an assembly that shares its
loads.** The suite runs in parallel and shares its loaded maps through lazy caches, so a test's
duration includes waiting on a load another test started, and which test pays for a load is
whichever asked first. One run therefore tags a different set each time: across four runs here the
set over a second was 118, 134, and then sixteen more that the first two had not shown. Tag what
reaches a second in ANY run; untag only what stays under in ALL of them. The list converges on
"every test that touches a shared map", which is the honest reading: under a mutant only that test
covers, it is the one that pays for the load.

**The conversions did not make the converted tests light.** The fourteen tests that opened a real
demo now build their state, and run in under a second each once the shared map is loaded - but the
first of them to run still pays that load, so most keep the tag. What a mutation run gains is the
suite's total, not their absence from the list.

