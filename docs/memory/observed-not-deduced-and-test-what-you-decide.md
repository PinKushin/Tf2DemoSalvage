---
name: observed-not-deduced-and-test-what-you-decide
description: A number or a conclusion counts only once it was observed; a decision reached by reasoning must be tested by something that tests that decision itself. Hooks enforce what a tool call can see.
metadata:
  type: feedback
---

Owner, 2026-10-21, after the lead quoted "~600 fps" for TF2 and "9x" as if measured (the only TF2 figure on record was another map's, plus his pub estimate): *"We need to get real numbers ... that's the scientific method, we need hooks that force you to use the Popperian method."* Then, narrowing it: *"I don't know that we really need a prediction first so much, as things need to be measured and actually observed, not deduced or induced. If you make any reasoning decision, that decision needs to be tested in a way that you are actually testing what you are deciding."* (typos fixed, by the lead.)

**Why:** a figure repeated from memory or a neighbouring measurement reads as established while being a guess; every gap this session that turned out wrong (the quiet-machine reading, the guided reviewer, the "it's the scanning" idea, the CI timeout fix that never reproduced locally) was a conclusion reached by reasoning and acted on before an experiment that could have contradicted it.

**How to apply:**
- A quantity is stated with the observation that produced it (file, command, environment, load before and after); otherwise it is called an estimate, with whose estimate it is.
- A decision made by reasoning (a cause, a fix, "this is the same as that") names the test that would show it wrong, runs it, and the test must exercise the decision itself: a repro that only slows things down is not a repro of a timeout; a control proves the instrument can see the thing.
- When a test cannot be made (the CI timeout never reproduced locally), say it is unverified and that CI is the only judge; do not call it a fix.
- Hooks enforce what a tool call can see (docs/measurements/, `fix:`/`perf:` commits carrying a test or an observation); they cannot judge whether a control is a good one.
Related: [[instrument-bugs-outnumber-decoder-bugs]], [[a-hook-is-not-live-until-a-control-fires-it]], [[measure-the-output-not-the-capability]].
