---
name: mutation-score-is-not-the-goal
description: Everything about running Stryker here — the baseline is a ratchet not a gate, the config fails silently in two ways, and a quarter of the code is invisible to it.
metadata:
  type: feedback
---

**Three memories were merged into this one on 2026-08-27** — `mutation-score-is-a-ratchet`,
`stryker-targetframework-must-be-in-csproj` and `stryker-globs-are-project-relative`. They were kept
apart while one of them *corrected* another, which is the worst possible arrangement: the claim
"80 is a floor" sat here and the correction sat in a file nobody reading this would open.

## The number: a ratchet, not a gate, and not 80

**The `tf2-core` mutation baseline is 52.16%**, measured on mutation-box on **2026-08-13** at sha
`0eb27f9`: 1,574 killed, 405 survived, 11 timeout, **1,049 with no coverage at all**.

**Do not quote 80 as a floor here.** That figure came from general guidance and was repeated in this
project as though it were a rule; the owner corrected it — no baseline had ever been established for
this codebase, and now the baseline is the measured one above. (The original 80 was stated for
`Cli.Tests`, which is the only project covering all of what it mutates, and it keeps it.)

**It is a ratchet in intent, not a gate.** The owner was explicit that it will not simply stay put:
the run happens daily or weekly, new code lands in between, and a week's work almost always lands
mutants nothing kills yet. So the score dropping is expected and routine — what is not optional is
**noticing and fixing it** rather than letting it drift down run after run.

**And the baseline itself is provisional.** The owner set it while iterating hard on features, and
said plainly that a large feature landing can drop it a long way — in which case the number gets
re-set rather than defended. What is being ratcheted is attention, not a specific figure. Treating
it as a hard gate would either block ordinary work or, worse, invite whatever change makes the
number go up fastest.

**The 1,049 uncovered mutants are the interesting number, not the 52.** A mutant with no coverage
is code no test reaches at all, which is a different problem from a test that fails to notice a
change — see [[most-of-a-decoder-is-untested]], where three of four sabotages survived because the
corpus never exercises those paths. Raising the score by writing tests for reachable-but-unkilled
mutants is worth doing; chasing uncovered ones may just mean deleting dead code.

## Two rules that matter more than the number

- **Don't trace dead ends.** A survivor that proves equivalent, or that sits in code whose
  behaviour nothing depends on, is done the moment that is established. One line of reasoning,
  then move on. Continuing is time spent proving what is already known.
- **Don't write tests for tests' sake.** A test written to kill a mutant, rather than to pin
  behaviour someone depends on, is a change-detector: it breaks on every future refactor and
  catches nothing. Rewriting production code so a mutant dies is the same mistake in disguise —
  `IsSupported` was rewritten for that reason and was borderline.

## Expect a lower score here than in a normal codebase

Owner's read, 2026-08-12, and it is a calibration rather than an excuse — worth having before anyone
reacts to a number here.

- **Much of it is renderers.** The trace writer, the JSON lines writer, the text dumper and the
  five assembly files exist to turn decoded structures into text. A mutant that changes a rendered
  string is often killable only by a change-detector test, which this project deliberately does
  not write.
- **A large part is exercised only by real demos.** Those tests live in the corpus project, which
  cannot be mutation tested at all (B34), so that code shows as `NoCoverage` rather than as
  survivors — 1,136 mutants on the 2026-08-12 run.
- **Safe mode removes whole methods from scoring.** One string-interpolation mutant that fails to
  compile takes every mutant in its method with it: 1,827 CompileError mutants on that run,
  concentrated in exactly those renderer files.

**So read the survivors by FILE, never the percentage.** On that run 149 of 242 survivors sat in
three files while everything else was in single digits — a specific, actionable finding that the
53% completely hides. See RISKS.md B35.

## The gate cannot see a quarter of the code

**444 mutants are removed before testing begins** — Stryker's safe mode drops mutations it
cannot compile, and this codebase is full of `ref struct BitReader` parameters its
instrumentation cannot wrap. That is concentrated in the decode core, the part that matters
most.

So a high score is evidence about the tested subset and silent about the rest. What actually
covers the decode paths is the corpus differential in `tools/differential/`, which compares
against another parser and runs in seconds. See [[differential-beats-fixtures]].

## Cadence: once a day at most, never per change, never on a disturbed tree

Owner's call, 2026-08-08, after three full runs in one evening cost about two and a half hours:
**stop mutating every time.** Use `dotnet stryker --since:main` during work (D13). Full runs are a
daily thing, or before a milestone — not before every merge, and never repeatedly in one session to
watch a number climb. That last one is exactly what happened: 92.79% → 97.34% → 99.37%, each run
telling less for the same hour. **I should have raised this rather than waiting for the owner to.**
Noticing that a loop has stopped paying is part of running it.

**The gate only means something on an undisturbed tree.** Four runs the same day: **98.12%**,
**92.93%**, **82.56%**, **99.57%**. Only the first and last meant anything. The middle two ran while
the session was editing files underneath Stryker, which builds and re-runs against the working tree.
The 82.56% run reported entire files as uncovered and finished in eight minutes against the usual
twenty-six — and the first bad number was treated as a real regression and cost a full pass. Run it
once, at the end of a work chunk, and touch nothing until it reports.

## A truncated run reports a plausible score

An earlier run printed *"All mutants have been tested"* and **37.74% in 11m16s**, having accounted
for **1,215 of 1,954** mutants. Per-file rows identical to the good run until it simply stops. The
percentage was internally consistent with the subset it held, so nothing looked wrong — and it was
reported upward as a real first measurement, with three conclusions built on it, one of which sized
a GitHub job timeout at 30 minutes against a workload that takes 33.

**Compare the accounted mutant total against a known-good run before believing a score.** Same rule
as reading a runner's `Total:` instead of its `Passed!` ([[read-the-trx-total-not-the-console]]), and
the reason a second run that disagrees is evidence about the FIRST one, not noise to average.

## `stryker-targetframework-must-be-in-csproj`

`TargetFramework` must be declared in each `.csproj`, never in `Directory.Build.props`. If it lives
in the props file, `dotnet stryker` aborts with *"Failed to analyze project builds. Stryker cannot
continue."* Verified 2026-08-07 with Stryker 4.16.0 and .NET SDK 10.0.302; net9.0 fails the same way,
so it is not a .NET 10 support gap.

Stryker's Buildalyzer step produces zero analyzer results and logs only *"No analyzer results to
log"* — no MSBuild error, nothing naming the TFM. The cause was found by bisecting
`Directory.Build.props` property by property. Everything else in that file
(`TreatWarningsAsErrors`, `AnalysisMode=All`, the `SonarAnalyzer.CSharp` PackageReference) is fine to
centralize; only the TFM breaks it.

**If a new project is added and `dotnet stryker` starts failing to analyze, check whether the TFM
drifted into the props file before investigating anything else.** Both `Directory.Build.props` and
each `.csproj` carry a comment saying the TFM must stay put — do not "tidy" it back into the props
file. See [[tf2demosalvage-build-gates]].

## `stryker-globs-are-project-relative`

Stryker resolves its path globs relative to **each project's own directory**, not the solution root
and not the directory `dotnet stryker` was invoked from. Owner-stated 2026-08-09. The official
configuration docs do not say this — they show `'**/*Services.cs'` and `MyFolder/MyService.cs{10..100}`
without ever naming the reference point.

```jsonc
"mutate": ["Schema/**/*.cs"]                               // correct
"mutate": ["managed/Tf2DemoSalvage.Core/Schema/**/*.cs"]   // matches nothing
```

**A glob that matches nothing does not error.** Stryker runs, reports a score, and the number looks
fine. Same failure shape as `actions/upload-artifact` finding no files — a green result that means
the step did nothing. A mutation score is only evidence if the mutants were actually placed where
you think.

**Open question, not yet checked empirically.** `tests/Tf2DemoSalvage.Core.Tests/stryker-config.json`
sets `since.ignore-changes-in` to `["**/*.md", "**/docs/**", "**/tools/**"]`. If those resolve
per-project, `docs/` and `tools/` are outside the mutated project entirely and can never match,
which would mean documentation-only commits do not get ignored and `--since:main` re-mutates
everything. That costs an hour rather than correctness, and it would look exactly like a normal
run — so confirm it at the next daily run by making a docs-only commit and checking whether Stryker
reports zero changed files. There is no `mutate` key in that config today, which is why this has not
bitten yet.

**`--since` targets must be branch names or full SHAs.** `--since:HEAD~3` fails, and fails *after*
doing all the work — fifteen minutes in, at report generation. `stryker-config.json` sets
`since.target` to `main` so the bare flag works; the default is `master`, which does not exist here.
Floor is about seven minutes, not instant.

## Where runs land

`~/measurements/<stamp>-<sha>-tf2-core/` on mutation-box, scheduled daily
(`~/tf2demosalvage/build/run-measurements.sh core`). The `stryker-core` runs on the same box belong
to PokemonBattleJournal — do not read their score as ours. Prune only by the `.owner` marker.

**After the D25 split, a per-project score is not what the old floor was set on.** `Core.Tests` and
`Corpus.Tests` both mutate `Tf2DemoSalvage.Core.csproj`, and Stryker scores the whole assembly
against whichever project is running — so corpus-only code is `NoCoverage` in the core run and vice
versa. `break: 80` on that project was a gate no amount of test-writing opens; it is set to 0, and
score the 100 NoCoverage out of that first run and it is 84.6% anyway. The tests are not the problem;
being asked to answer for corpus-only code is.

See [[tests-before-codecs]] — writing tests after the code is what produces the survivors.
