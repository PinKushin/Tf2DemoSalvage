// Per-test isolation AND in-process parallelism, the unit/integration half of this repo's two-tier
// rule (docs/memory/nunit-shared-fixture-is-the-standard.md).
//
// **This assembly used to opt OUT of both, and the reason has expired.** The previous version of
// this file argued the UI side of the split: a fixture holding a launched application wants a shared
// instance, and in-process parallelism on a single desktop is unsafe rather than slow. All of that
// is still true, and none of it applies here any more — the UI tests live in
// Tf2DemoSalvage.Viewer3D.UiTests, which is a separate assembly with its own ViewerSession fixture.
// Nothing left in this project constructs a form, shows a window, or attaches to a running viewer.
//
// The old comment even said so, in a sentence that dated it: "today's four tests construct forms
// without showing them". There are now 278 tests here and none of them constructs a form at all.
// The assembly grew into an ordinary unit and integration suite while its policy went on describing
// the four tests it started with, and the only symptom was two minutes on the clock.
//
// **That is the lesson worth keeping**: a rationale written for what an assembly WILL become is
// correct exactly until it becomes something else, and nothing fails when it stops being true. The
// UI-assembly reasoning was right when it was written and had no way to announce that it no longer
// was. Anything reserved for "what comes next" needs re-reading when next arrives.
//
// The two settings belong together. NUnit shares one fixture instance across a fixture's tests by
// default, so a field one test mutates leaks into its siblings: harmless while the suite is serial,
// a race the moment it is not.
//
// A fixture that genuinely needs serial execution — one that writes a shared file, or drives
// anything global — carries [NonParallelizable] itself, which keeps the exception beside the code
// it affects.
[assembly: Parallelizable(ParallelScope.All)]
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
