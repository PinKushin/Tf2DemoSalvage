// Parallelism and fixture lifetime are DELIBERATELY not set here, and this file exists to say so
// rather than leaving the absence looking like an oversight.
//
// The migration off xunit.v3 makes both of these a choice for the first time, because NUnit's
// defaults differ from xUnit's in two ways:
//
//   - **Parallelism is opt-in.** xUnit parallelises by collection automatically; NUnit runs
//     serially unless told otherwise. So a migrated suite goes quiet-slow rather than failing,
//     which is worth watching for on the big projects.
//   - **One fixture instance is shared by every test in it.** xUnit constructs the class per
//     test. `[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]` restores that.
//
// **Neither is set assembly-wide, because this assembly is the one that will grow UI tests.**
// A UI fixture wants the OPPOSITE of both: one application instance shared across its tests,
// because launching the app and attaching a driver is the expensive part, and strictly no
// parallelism, because UI tests drive a single desktop and a second one stealing focus mid-run
// delivers clicks into whatever is now in front.
//
// **The shared fixture is the standard here, not a default nobody chose.** Owner's preference,
// and the UI case is why: a fixture that holds a launched application and an attached driver is
// the normal shape of an expensive test, and per-test construction would pay that cost again for
// every test in the class. A fixture that genuinely needs per-test isolation carries the
// attribute itself, so the exception sits next to the code it affects and has to be justified
// there.
//
// The four tests here construct forms without showing them, so they are fast, isolated, and gain
// nothing measurable from running in parallel.
