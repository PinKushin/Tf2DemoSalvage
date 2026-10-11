// Per-test isolation AND in-process parallelism, the unit/integration half of this repo's two-tier
// rule (docs/memory/nunit-shared-fixture-is-the-standard.md).
//
// **This file did not exist until 2026-10-11, and the suite ran SERIALLY for the whole of that time
// while three documents said it was parallel** (D213). These tests were split out of Viewer3D.Tests
// (B184); that assembly's AssemblyTestPolicy.cs stayed behind, and assembly attributes do not travel
// with the files that rely on them. NUnit's default with no attribute is one test at a time. The
// evidence was in every .trx: 1,163 seconds of summed test time in 19 m 22 s of wall clock — summed
// equal to wall is what serial looks like. `MapCache`'s own remarks quote the attribute as this
// assembly's, which is how it was believed.
//
// The lesson is the one Viewer3D.Tests' policy file already draws: a statement about an assembly
// cannot fail when it stops being true. Check the attribute, not the comment about it.
//
// A fixture that genuinely needs serial execution carries [NonParallelizable] itself, which keeps
// the exception beside the code it affects. Every fixture gets its own instance per test, so a
// field one test mutates cannot leak into a sibling running beside it.
[assembly: Parallelizable(ParallelScope.All)]
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]

// **A cap on workers, for the reason Viewer3D.Tests has one** (B185): these tests load whole maps,
// and one worker per processor holds more of them at once than the machine has memory for. The
// number is measured — see docs/DECISIONS.md D213 for the runs.
[assembly: LevelOfParallelism(3)]
