// Per-test isolation AND in-process parallelism, which is the unit/integration half of this
// repo's two-tier rule (docs/memory/nunit-shared-fixture-is-the-standard.md).
//
// The two belong together: isolation is what makes the parallelism safe. NUnit shares one fixture
// instance across a fixture's tests by default, so a field one test mutates leaks into its
// siblings - harmless while the suite is serial, a race the moment it is not.
//
// This is also the behaviour these tests were written under. xUnit constructs the test class once
// per test, so keeping it is preserving the contract they already assumed rather than adopting a
// new one.
//
// NOT for a UI assembly: see the note in Tf2DemoSalvage.Viewer3D.Tests for why a fixture holding
// a launched application wants the opposite of both settings.
[assembly: Parallelizable(ParallelScope.All)]
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
