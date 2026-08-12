// Per-test isolation AND in-process parallelism, which is the unit/integration half of this
// repo's two-tier rule (docs/memory/nunit-shared-fixture-is-the-standard.md).
//
// The two belong together: the isolation is what makes the parallelism safe. NUnit shares one
// fixture instance across a fixture's tests by default, so a field one test mutates leaks into
// its siblings - harmless while the suite is serial, a race the moment it is not.
//
// It is also the behaviour these tests were written under. xUnit constructs the test class once
// per test, so keeping it preserves the contract they already assumed rather than adopting a new
// one mid-migration.
//
// **Parallelism is opt-in in NUnit and was automatic in xUnit**, which is this migration's one
// silent regression risk: without the first line the suite still passes, just serially. That is
// invisible in a pass/fail result and very visible in the wall clock.
//
// NOT for a UI assembly: see Tf2DemoSalvage.Viewer3D.Tests for why a fixture holding a launched
// application wants the opposite of both settings.
[assembly: Parallelizable(ParallelScope.All)]
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
