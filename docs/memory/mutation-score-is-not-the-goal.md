---
name: mutation-score-is-not-the-goal
description: 80 is a floor, not a target to beat — don't chase a higher score, don't trace dead ends, and don't write tests that exist only to kill mutants
metadata:
  type: feedback
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

## `--since` targets must be branch names or full SHAs

`--since:HEAD~3` fails, and fails *after* doing all the work — fifteen minutes in, at report
generation. `stryker-config.json` sets `since.target` to `main` so the bare flag works; the
default is `master`, which does not exist here. Floor is about seven minutes, not instant.

See [[tests-before-codecs]] — writing tests after the code is what produces the survivors.
