# Measurements: observe, ask, measure, say what was seen

The owner, 2026-10-21: *"We need to get real numbers ... that's the scientific method."* And, narrowing it: nothing counts as known
until it has been observed, and a decision reached by reasoning must be tested by something that tests that decision itself. He
also pointed out that the method does not start from a hypothesis conjured from nothing: **an initial observation raises a
question, and the question comes before the method used to answer it.** This folder is where the answering is recorded.

**One file per measurement, named `YYYY-MM-DD-<subject>.md`.** Sections, in this order:

1. `## Observation` - what was seen that raised the question (a number someone quoted, a stall in a log, a screenshot). Say whose
   observation it is and how it was made; an estimate is called an estimate.
2. `## Question` - what we want to know, in a form a measurement can answer.
3. `## Method` - the exact commands and settings, and the instrument's `## Control`: what it was asked for that must be present or
   known, so a silent instrument is distinguishable from an empty subject.
4. `## Environment` - machine, resolution, load before and after, the commits or builds the run used.
5. `## Result` - the measured values, with the raw output pointed to or pasted, never summarised away.
6. `## Verdict` - what the observation answers, one sentence, and what changes because of it.

**Optional, and welcome when there is something to be wrong about:** `## Hypothesis` and `## Prediction` (numbers fixed before the
run, with what would falsify them). They are not required; the observation is.

A figure quoted later in a finding, RISKS entry or release note cites the file that observed it. A prediction that turned out wrong
is not edited into the right answer; the verdict says how wrong it was.
