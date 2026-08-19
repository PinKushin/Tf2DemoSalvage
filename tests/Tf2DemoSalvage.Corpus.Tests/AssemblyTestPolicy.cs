// WHAT BELONGS IN THIS ASSEMBLY
//
// **A test lives here when its evidence requires real engine bytes, and nowhere else.** The
// project is a category, not a staging area — it is not being emptied out, it is being narrowed
// to the claims only a recording can settle.
//
// Two kinds qualify, and both are things a written demo structurally cannot supply:
//
//   1. **Corroboration between paths that share no code.** A sound index comes out of a
//      delta-coded bit stream and the soundprecache table comes out of svc_CreateStringTable;
//      the index landing inside the table is a fact about the FILE. Write both sides synthetically
//      and the test checks this project against its own beliefs, which is not a check.
//   2. **Totality.** The engine wrote these bytes and reads them back, so anything here that
//      fails to decode is our defect (docs/memory/decode-must-be-total.md). A synthetic body
//      proves the decoder handles the shapes someone thought to write; only a real file can carry
//      a shape nobody thought of.
//
// Everything else belongs in Tf2DemoSalvage.Core.Tests as a synthetic demo. In particular:
// field widths, protocol boundaries, delta bases, sign handling, and anything asserted as a
// plausibility RANGE. A range is what a corpus test reaches for when it has no ground truth —
// "the origin is inside the world" — and a synthetic demo simply knows the answer, so it asserts
// the value. That is a stricter claim, not a weaker substitute.
//
// The practical reason for the split is the owner's: this assembly needs 305 MB of Git LFS, so it
// cannot run on the measurement boxes and costs bandwidth on every CI job. Tests here skip when
// the corpus is absent, which is what keeps the rest of the suite runnable anywhere.
//
// **Why the synthetic replacements go to Core.Tests rather than staying here, which is the
// obvious alternative and would keep related tests together.** Stryker runs per project, and the
// `corpus` mutation route is permanently closed in the measurement-box schedule: coverage capture
// alone took 22+ minutes on one core, and a suite of integration tests over ten 20 MB files is a
// weak mutation harness at any runtime, because it only exercises the paths those ten demos
// happen to take. `core` mutates in ~22 minutes with real coverage capture and runs daily.
//
// So a synthetic test placed in this assembly is a test nothing ever mutates — nothing checks
// whether it can fail. That is the deciding reason. The naming agrees with it: this project is
// named for its DATA SOURCE, and a synthetic test has no corpus in it.
//
// This would flip if the assembly ever shrank enough to be worth booking a mutation slot for.
//
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
