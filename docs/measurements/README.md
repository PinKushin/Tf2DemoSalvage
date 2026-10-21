# Measurements: predict first, then measure, then say what was falsified

The owner, 2026-10-21: *"we need to get real numbers ... that's the scientific method, we need hooks that force you to use the
Popperian method."* This folder is the record that makes a measurement a test of something, rather than a number looked for.

**One file per measurement, named `YYYY-MM-DD-<subject>.md`.** It has these sections, in this order, and the first six are
written BEFORE the run and never edited afterwards:

1. `## Question` - what we want to know.
2. `## Hypothesis` - a statement that can be wrong, with the alternative that would replace it.
3. `## Prediction` - the numbers, as ranges, fixed before the run.
4. `## Falsified if` - the observations that would show the prediction wrong, stated in numbers.
5. `## Control` - what must hold for the run to count, and what would void it.
6. `## Method` - the exact commands and settings.
7. `## Environment` - filled by the run (machine, load before and after, commits).
8. `## Result` - the measured values, with the raw output pointed to, never summarised away.
9. `## Verdict` - `confirmed`, `falsified` or `inconclusive`, one sentence, and what changes because of it.

A prediction that was falsified is not edited into the right answer. The verdict says it was wrong and by how much.
Later claims in findings, RISKS and RELEASE-NOTES that quote a number cite the file that measured it.
