---
name: nunit-shared-fixture-is-the-standard
description: NUnit's one-instance-per-fixture lifetime is the deliberate standard here; do not add InstancePerTestCase by default
metadata:
  type: feedback
---

**Do not set `[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]`, and do not set
`[assembly: Parallelizable]` on a UI test assembly.** Owner's preference, stated 2026-08-12
during the migration off xunit.v3.

The reasoning is the UI case, and it generalises. A UI fixture holds a launched application and
an attached driver; that setup is the expensive part of the test, so **one instance shared by
every test in the fixture is the point**, not an accident of NUnit's defaults. Per-test
construction pays the launch cost again for every test in the class.

Parallelism is worse than merely slow there: UI tests drive a single desktop, and a second suite
stealing focus mid-run does not fail the click, it delivers it into whatever is now in front.

**Why this needs writing down:** migrating from xUnit makes it look like a regression to fix.
xUnit constructs the test class once per test, so a mechanical migration invites "restore the old
behaviour" as an assembly-wide attribute — which is exactly wrong for the fixtures that matter
most. It was proposed and rejected in the same session it was proposed.

A fixture that genuinely needs per-test isolation carries the attribute itself, so the exception
sits beside the code and has to be justified there. See also [[tests-before-codecs]] for the
other standing rule about how tests are written here.
