---
name: nunit-shared-fixture-is-the-standard
description: Fixture lifetime is per test-KIND, not per repo - isolation plus parallelism for unit/integration, one shared fixture and CI matrices for UI
metadata:
  type: feedback
---

**Two tiers, and applying either one everywhere is wrong.** Owner's rule, stated 2026-08-12
during the migration off xunit.v3.

| Test kind | Fixture lifetime | Parallelism |
|---|---|---|
| Unit, integration | `[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]` — per-test isolation | In-process, `[assembly: Parallelizable]` |
| UI | **Shared fixture**, NUnit's default | **None in-process.** Parallelise with CI matrices instead |

**Per-test isolation is what makes in-process parallelism safe**, so for unit and integration
suites the two go together — that is the whole reason to want it, not a stylistic preference. The
xUnit behaviour being migrated away from (a new class instance per test) is the right behaviour
for those projects, and restoring it there is correct.

**A UI fixture is the opposite case.** It holds a launched application and an attached driver, and
that setup is the expensive part of the test — sharing one instance across the fixture's tests is
the point. Per-test construction pays the launch cost again for every test.

In-process parallelism is not merely slow for UI tests, it is unsafe: they drive a single desktop,
and a second run stealing focus mid-click does not fail the click, it delivers it into whatever is
now in front. **Parallelise UI tests across CI matrix legs** — separate machines or VMs, each with
its own desktop — never across threads in one process.

**Why this needs writing down:** a mechanical migration invites one assembly-wide attribute
applied uniformly, and either uniform choice is wrong for half the suite. The first draft of this
entry made exactly that mistake in the safe-looking direction, banning per-test isolation
everywhere, which would have quietly serialised a 770-test suite.

See also [[tests-before-codecs]].
