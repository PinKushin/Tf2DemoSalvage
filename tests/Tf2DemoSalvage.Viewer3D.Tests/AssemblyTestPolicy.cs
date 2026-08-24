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
// **And it happened again, to this comment, within days (B178).** The paragraph above says "none of
// them constructs a form at all". Six fixtures do — FullScreenTests, LoadedDemoTests,
// MainFormTests, MainFormDisposeTests, ShowPositionsTests and ViewerSettingsTests — and they
// arrived without anything objecting, because a stale comment cannot fail.
//
// The cost was not cosmetic. `ParallelScope.All` on an MTA worker pool means Windows Forms being
// constructed concurrently off the STA, each owning a D3D swap chain and an OpenAL context whose
// current-context is process-wide. The test host crashed in roughly half of all runs, at three
// unrelated native sites, and the standing explanation in build/gate.sh blamed the desktop.
//
// The policy below was never wrong and did not need changing. Its escape hatch — a fixture that
// needs serial execution carries [NonParallelizable] itself — is exactly the fix, and those six
// now carry it along with [Apartment(ApartmentState.STA)]. What failed was nobody checking whether
// the comment still described the assembly.
//
// **So: adding a test here that constructs a form means marking that fixture.** There is no
// assembly-wide setting that would catch it for you, deliberately — the rest of this suite is
// hundreds of fast parallel tests and serialising all of them to protect six would cost minutes.
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
