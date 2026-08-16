// Per-test isolation AND in-process parallelism, the unit/integration half of this repo's two-tier
// rule (docs/memory/nunit-shared-fixture-is-the-standard.md).
//
// **This file was missing, and the cost was invisible.** Every other unit and integration assembly
// here carries these two lines; this one did not, so its 357 tests ran one after another while the
// rest of the suite used the whole machine. Nothing reported it, because a serial run and a parallel
// run produce identical pass/fail output - the only symptom is the wall clock, which is exactly the
// failure the Core assembly's copy of this file warns about in writing:
//
//     "Parallelism is opt-in in NUnit and was automatic in xUnit ... without the first line the
//      suite still passes, just serially. That is invisible in a pass/fail result and very visible
//      in the wall clock."
//
// It went unnoticed because this assembly's tests are individually slow for a real reason - they
// decompress BSP lumps and decode textures - so "content tests take a while" was a plausible
// explanation for a number that had a second cause underneath it.
//
// The two settings belong together. NUnit shares one fixture instance across a fixture's tests by
// default, so a field one test mutates leaks into its siblings: harmless while the suite is serial,
// a race the moment it is not.
//
// Safe here because nothing in this assembly touches a desktop, and the SDK reference it reads is a
// fixed checkout behind concurrent caches. A fixture that genuinely needs serial execution can carry
// [NonParallelizable] itself, which keeps the exception beside the code it affects.
[assembly: Parallelizable(ParallelScope.All)]
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
