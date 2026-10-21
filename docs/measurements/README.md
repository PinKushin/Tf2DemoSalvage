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

## Hooks: what is enforced, and what is not

Two project hooks hold this. Each script's header carries its reasoning, and each has a `*.sequence-test.ps1` with controls.

- `tf2-measurement-gate.ps1` (PreToolUse, Bash/PowerShell/`mcp__tf2__console`):
  - **Before a measuring command**: a COMMITTED file here must have Observation, Question, Method and Control filled, and Result still empty. Measuring commands are `tf2demoview --measure|--shot`, `dotnet-trace collect|report`, a measuring Probe, and `cl_showfps 1`, `net_graph 1` or `timedemo` in the TF2 console.
    - `# measurement: <file>` picks the file.
    - `# exploratory` skips the gate (`// exploratory` in the console), and its numbers may not be quoted.
    - A missing Prediction is only a note.
  - **Before `git commit`, `git push` or `gh release`**:
    - a filled Result needs a Verdict;
    - a file with a Prediction needs confirmed, falsified or inconclusive in its Verdict;
    - no section that was filled at first commit may differ now.
  - **A `fix:` or `perf:` commit**:
    - needs tests/ staged, or `Tested by:` plus `Observed:`, or `No-test: <reason>`;
    - `perf:` also cites its `docs/measurements/` file.
  - Any commit message saying "fixed" or "faster" beside an uncited figure is refused.
- `tf2-measurement-cited.ps1` (Edit/Write): a newly added line with a figure (fps, ms, MB per frame) and no `measurements/<file>.md` or `finding NN` on the same line is handled by file:
  - in `RELEASE-NOTES.md` it is refused;
  - in README, RISKS or findings it draws a warning.

**Not enforceable by a hook:**
- that a test really tests the decision, or was red first;
- that a Control is a good one;
- that a pasted Result is what the instrument printed;
- `git commit -F` and editor commits, which are not read.

The hooks check that the record exists. Reading the record is still review.
