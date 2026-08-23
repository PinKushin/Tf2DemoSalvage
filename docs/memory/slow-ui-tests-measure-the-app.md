---
name: slow-ui-tests-measure-the-app
description: UI Automation can only go as fast as the message loop it queries, so per-test durations are a performance measurement of the application.
metadata:
  type: project
---

**A UI suite that got slow is telling you the application got slow.** UIA queries are served by the
target's message loop, so a viewer spending 57 ms a frame answers `Find` at 57 ms granularity, and a
five-second wait becomes a fifty-second one.

Measured 2026-08-23: adding a second demo to the UI session took the suite from 12 s to 4m43. Three
explanations were proposed and the first two were wrong —

1. the demo decode (it was 0.4–0.8 s, not the 20 s of asset loading beside it);
2. the log reader re-reading a 45 MB file on every poll (real, worth fixing, not the cause).

**What settled it was `--logger "console;verbosity=normal"`, which prints a duration per test.**
Every test running *before* the demo-switch test took under two seconds; every test *after* it took
23–58 s. That pattern names the cause on its own: nothing about those tests changed, so the thing
they share — the application — did. The render log then gave the number: **300 frames a second
before the switches, 19 after**, paused, with posing and lighting at zero. Filed as B148.

**Read the per-test durations before touching the tests.** A suite that slowed down is a measurement
you already paid for; treating it as test flakiness throws the finding away and usually adds a
timeout to hide it.

See [[measure-every-hop-before-blaming-one]] — this is that rule with the hops being phases of a
load — and [[a-log-must-name-what-it-measured]], since the first wrong guess picked the one phase
that already happened to be wrapped in a timer.
