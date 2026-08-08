---
name: mutation-score-is-not-the-goal
description: Zero survivors is not the bar and will not scale — read the survivors, keep break at 80, and never run the gate against a tree you are still editing
metadata:
  type: feedback
---

The owner flagged this on 2026-08-08, while I was closing survivors one at a time to reach a
clean sweep: chasing a high mutation score **will become impossible as the project grows**, and
matching another project's thresholds is the wrong target.

**Why:** a small codebase can reach zero survivors because most survivors turn out to be
equivalent mutants that can be deleted or suppressed with a reason. That stops being true as
code grows. Past some point the remaining survivors are neither worth killing nor cleanly
equivalent, and chasing them starts damaging the code — rewriting a method purely so a mutant
dies is a change-detector test wearing a disguise. `IsSupported` was rewritten today for
exactly that reason, and it was borderline.

**How to apply:** `break: 80` in `stryker-config.json` is the real gate; it predates this
session and should not be raised to chase a number. Report *what* survived and why it is
acceptable, rather than reporting a percentage or treating any survivor as blocking. This is
D6's "read the survivors, not the score" — follow it literally.

## The gate only measures what it claims on an undisturbed tree

Three runs the same day: **98.12%**, then **92.93%**, then **82.56%**, then **99.57%**. Only the
first and last meant anything. The middle two were measured while this session was editing
files underneath Stryker — it builds and re-runs against the working tree, so concurrent edits
corrupt coverage collection. The 82.56% run reported entire files as uncovered and finished in
eight minutes against the usual twenty-six.

I treated the first bad number as a real regression and spent a full work pass on it. The tests
from that pass were worth keeping on their own merits, but the number that triggered it was
noise.

**Run the gate once, at the end of a work chunk, and touch nothing until it reports.**

## `--since` targets must be branch names or full SHAs

`--since:HEAD~3` fails — Stryker's git layer cannot resolve that syntax, and it fails *after*
doing all the work, so 15 minutes are wasted before the error appears. Use a branch name
(`--since:main`) or a full commit SHA. `stryker-config.json` sets `since.target` to `main` so
the bare `--since` flag works; the default target is `master`, which does not exist here.

See [[tests-before-codecs]] for the related lapse — writing tests after the code is what
produces the survivors in the first place.
