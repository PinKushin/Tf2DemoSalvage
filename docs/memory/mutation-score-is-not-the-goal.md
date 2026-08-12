---
name: mutation-score-is-not-the-goal
description: "Run the full gate once a day at most, never per change — 80 is a floor not a target, and safe mode hides a quarter of the code from it"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-10T10:18:40.060Z
---

The owner's position, given 2026-08-08 while I was closing survivors one at a time toward a
clean sweep, and refined immediately after when I over-swung the other way and wrote it up as
"know when to lower it" with no floor.

**The actual point:** the score is not a target to chase upward, and **80 is a floor**. `break`
stays at 80. There is nothing to gain from driving it higher — that is where the busywork lives
— but falling below 80 is a smell worth investigating rather than a setting to relax. The
threshold works in both directions.

**Why:** a small codebase reaches zero survivors because most of them are equivalent mutants
that can be deleted or suppressed. That stops being true as code grows.

**How to apply — two rules that matter more than the number:**

- **Don't trace dead ends.** A survivor that proves equivalent, or that sits in code whose
  behaviour nothing depends on, is done the moment that is established. One line of reasoning,
  then move on. Continuing is time spent proving what is already known.
- **Don't write tests for tests' sake.** A test written to kill a mutant, rather than to pin
  behaviour someone depends on, is a change-detector: it breaks on every future refactor and
  catches nothing. Rewriting production code so a mutant dies is the same mistake in disguise —
  `IsSupported` was rewritten for that reason and was borderline.

## The gate only means something on an undisturbed tree

Four runs the same day: **98.12%**, **92.93%**, **82.56%**, **99.57%**. Only the first and last
meant anything. The middle two ran while this session was editing files underneath Stryker,
which builds and re-runs against the working tree. The 82.56% run reported entire files as
uncovered and finished in eight minutes against the usual twenty-six.

I treated the first bad number as a real regression and spent a full pass on it. Run the gate
once, at the end of a work chunk, and touch nothing until it reports.

## Cadence: once a day, never per change

Owner's call, 2026-08-08, after three full runs in one evening cost about two and a half hours:
**stop mutating every time.** A full run is now 43-48 minutes against 505 tests and grows with
every feature, because the cost is tests times mutants.

Use `dotnet stryker --since:main` during work (D13). Full runs are a daily thing, or before a
milestone — not before every merge, and never repeatedly in one session to watch a number climb.
That last one is exactly what happened here: 92.79% -> 97.34% -> 99.37%, each run telling less
for the same hour.

**I should have raised this rather than waiting for the owner to.** The signal was there after
the second run: the score was already well past the threshold and the remaining findings were
small. Noticing that a loop has stopped paying is part of running it.

## The gate cannot see a quarter of the code

**444 mutants are removed before testing begins** — Stryker's safe mode drops mutations it
cannot compile, and this codebase is full of `ref struct BitReader` parameters its
instrumentation cannot wrap. That is concentrated in the decode core, the part that matters
most.

So a high score is evidence about the tested subset and silent about the rest. What actually
covers the decode paths is the corpus differential in `tools/differential/`, which compares
against another parser and runs in seconds. See [[differential-beats-fixtures]].

## `--since` targets must be branch names or full SHAs

`--since:HEAD~3` fails, and fails *after* doing all the work — fifteen minutes in, at report
generation. `stryker-config.json` sets `since.target` to `main` so the bare flag works; the
default is `master`, which does not exist here. Floor is about seven minutes, not instant.

## After the D25 split, a per-project score is not the thing the floor was set on

The 80 floor above was stated when ONE test project covered everything it mutated. That is no
longer the shape. `Core.Tests` and `Corpus.Tests` both mutate `Tf2DemoSalvage.Core.csproj`, and
Stryker scores the whole assembly against whichever project is running — so corpus-only code is
`NoCoverage` in the core run and vice versa. First core-only run, on the Oracle box 2026-08-10:
113 killed, 98 survived, 7 timeout, 100 no-coverage, **37.74%**. The 113 kills prove the runner
worked, so the number is honest; it just is not a measure of the suite.

`break: 80` on that project is a gate no amount of test-writing opens, and once the runner stopped
swallowing exit codes it fired nightly. Set to 0 pending the owner's call — **flagged to them as a
conflict with their own floor, not decided unilaterally.** The way to keep the floor is a config
whose `test-projects` lists BOTH, run weekly; the daily core run then stays a fast partial signal.
Unmeasured cost, which is why it was proposed rather than done.

`Cli.Tests` keeps 80 and should: it is the only project covering all of what it mutates.

**Score the 100 NoCoverage out and the same run is 84.6%** — above the floor. The tests are not
the problem; being asked to answer for corpus-only code is.

## The first number reported from that run was 37.74%, and it was not a measurement

An earlier run the same morning printed *"All mutants have been tested"* and **37.74% in 11m16s**,
having accounted for **1215 of 1954** mutants. Per-file rows identical to the good run until it
simply stops. The percentage was internally consistent with the subset it held, so nothing looked
wrong — and it was reported upward as a real first measurement, with three conclusions built on
it, one of which sized a GitHub job timeout at 30 minutes against a workload that takes 33.

**Compare the accounted mutant total against a known-good run before believing a score.** Same
rule as reading a runner's `Total:` instead of its `Passed!`, and the reason a second run that
disagrees is evidence about the FIRST one, not noise to average.

See [[tests-before-codecs]] — writing tests after the code is what produces the survivors.

## This project specifically: expect a lower score than a normal codebase

Owner's read, 2026-08-12, and it is a calibration rather than an excuse - worth having before
anyone reacts to a number here.

`core` scored **53-54 %** across its first two full runs and that is roughly what this codebase
should score, because of what it is:

- **Much of it is renderers.** The trace writer, the JSON lines writer, the text dumper and the
  five assembly files exist to turn decoded structures into text. A mutant that changes a rendered
  string is often killable only by a change-detector test, which this project deliberately does
  not write.
- **A large part is exercised only by real demos.** Those tests live in the corpus project, which
  cannot be mutation tested at all (B34), so that code shows as `NoCoverage` rather than as
  survivors - 1136 mutants on the 2026-08-12 run.
- **Safe mode removes whole methods from scoring.** One string-interpolation mutant that fails to
  compile takes every mutant in its method with it: 1827 CompileError mutants on that run,
  concentrated in exactly those renderer files.

**So read the survivors by FILE, never the percentage.** On that run 149 of 242 survivors sat in
three files while everything else was in single digits - a specific, actionable finding that the
53 % completely hides. See RISKS.md B35.
