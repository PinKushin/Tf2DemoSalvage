---
name: an-assembly-attribute-does-not-travel-with-its-tests
description: Tests split into a new assembly leave [assembly:] attributes behind; Rendering.Tests ran serially for weeks while its docs said parallel. Check the attribute in the assembly, and read summed-vs-wall in the trx.
metadata:
  type: feedback
---

**`[assembly: Parallelizable]`, `FixtureLifeCycle`, `LevelOfParallelism` live in ONE file per assembly
(`AssemblyTestPolicy.cs`). Moving test files to a new project does not move them.**

**What happened (D213, 2026-10-11).** `Rendering.Tests` was split out of `Viewer3D.Tests` (B184). The
policy file stayed behind. NUnit's default with no attribute: one test at a time. `MapCache`'s remarks,
D213 and a memory entry all said the assembly was `ParallelScope.All`. Nothing failed; it cost 19 minutes
a run.

**The tell, in any `.trx`:** summed test durations ≈ wall clock means serial. 1,163 s summed in 19 m 22 s.
Parallel at N workers shows summed ≈ N × wall.

**How to apply:**

- New test project, or tests moved between projects: check the destination has `AssemblyTestPolicy.cs`.
  Grep the ATTRIBUTE (`assembly: Parallelizable`), not prose that quotes it.
- A statement about an assembly cannot fail when it stops being true (same lesson as
  `Viewer3D.Tests/AssemblyTestPolicy.cs`'s own header).
- Turning parallelism on does little while tests each load their own map: three workers took 932 s against
  1,163 s serial, each test 2.4× slower under contention. Sharing immutable loads is what pays
  ([[nunit-shared-fixture-is-the-standard]]).
- With shared lazy caches, a per-test duration includes WAITING on a load another test started — the
  first toucher is random. Re-derive `MutationHeavy` from the union of two runs, not one.
- Still without a policy file when this was written: `Scene.Tests`, `Animation.Tests`,
  `Presentation.Tests`, `Fonts.Tests`, `Logging.Tests`. Not changed; each needs its own two-run proof.
