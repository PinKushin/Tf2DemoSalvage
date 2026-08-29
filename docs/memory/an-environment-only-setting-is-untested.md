---
name: an-environment-only-setting-is-untested
description: A setting reachable only through an environment variable cannot be tested without changing the whole run, so nothing tests it. Autoplay had one reference in the repo and broke three times.
metadata:
  type: feedback
---

**Before adding a setting that only an environment variable can reach, ask which test will set it.
If the answer is "a test would have to change the whole run", it is an option, not a variable.**

**Why:** a process-wide variable is process-wide. A test that sets one sets it for every other test
in the same process — including the ones whose whole point is that the behaviour is OFF. So the
setting ends up with no coverage, and the absence is invisible because everything around it is
green.

Measured on 2026-08-29: `TF2VIEW_AUTOPLAY` had **exactly one reference in the entire repository —
its own declaration.** No script set it, no test set it, no CI job set it, no document mentioned it.
Its ordering requirement then broke **three separate times**, twice recorded in `DemoSystems.Open`'s
own remarks and the third found only by launching the viewer and reading the log (B223, D118).

The trap is that the reasoning *for* the variable is sound at every step. The comment beside it read
*"a system that read one could not be tested without setting it for the whole run"* — correct — and
concluded that the WINDOW should be the one place that reads the environment. Also correct, and it
answers a different question. Nothing in that chain asks whether the SETTING is tested, only where
the read belongs.

**How to apply:** make it an option or a config command; keep the variable working alongside if one
already exists, because a shell somewhere may export it and dropping it is a silent regression. Then
the test is `new MainForm("--autoplay", path)` — one line, isolated, per-launch.

The general shape: **a design that is defensible locally can still leave a feature with zero
observers.** Count the references before trusting the design.

Related: [[output-level-assertion-or-it-is-not-done]],
[[three-test-levels-and-the-third-is-missing]], [[measure-the-output-not-the-capability]],
[[a-null-object-default-hides-a-missed-wiring]], [[one-place-or-it-drifts]].
