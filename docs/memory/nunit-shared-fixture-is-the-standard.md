---
name: nunit-shared-fixture-is-the-standard
description: Fixture lifetime is per test-KIND, not per repo - isolation plus parallelism for unit/integration, one shared fixture and CI matrices for UI
metadata:
  type: feedback
---

**Two tiers; applying either everywhere is wrong.**

| Test kind | Fixture lifetime | Parallelism |
|---|---|---|
| Unit, integration | `InstancePerTestCase` — per-test isolation | In-process, `[assembly: Parallelizable]` |
| UI | Shared fixture (NUnit default) | None in-process — CI matrices instead |

**Per-test isolation makes in-process parallelism safe** for unit/integration — that's the whole
reason to want it. A UI fixture holds a launched app + driver, the expensive part — sharing across a
fixture's tests is the point; per-test construction pays the launch cost repeatedly. In-process
parallelism is UNSAFE for UI tests (one desktop; a second run stealing focus mid-click delivers the
click elsewhere) — parallelise across CI matrix legs (separate machines), never threads.

**This project isn't expected to need the matrix** — one main window, small enough to run serially.

## The shared fixture also FINDS a class of bug

A layout bug (leaving full screen re-added a control via `Controls.Add`, appending and swapping dock
order) passed alone and failed only in a full run — a per-test application would never reach the
state that caused it. **Per-test isolation actively hides state-leak defects**, and a long-lived UI
is made of exactly that kind of state. The same bug is findable with per-test isolation only by
stuffing the whole sequence into one test — a shared fixture buys that sequence coverage
incidentally, keeping tests one-thing-each while combinations still get exercised.

See also [[tests-before-codecs]].
