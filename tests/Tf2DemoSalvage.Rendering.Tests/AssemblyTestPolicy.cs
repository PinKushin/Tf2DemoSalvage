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
// and one worker per processor would start more loads at once than the machine has memory for.
//
// Three, measured 2026-10-11 with the loads shared (`MapCache`, `SharedGame`), the test host's
// working set sampled every five seconds:
//
//     3 workers   336 s wall   16.1 GB peak
//     6 workers   274 s, 272 s   16.8 GB, 18.0 GB peak
//
// Every run passed, so this is not a correctness choice. Six buys a minute and spends up to two
// gigabytes of the margin under the 21 GB B185 found to be the ceiling, on a machine that is often
// also running the game. The peak is mostly what the caches RETAIN — one whole map per distinct
// request, for the life of the run — so bounding what `MapCache` keeps is what would lower it, and
// a new distinct request adds its whole map to that figure: prefer an existing one.
[assembly: LevelOfParallelism(3)]
