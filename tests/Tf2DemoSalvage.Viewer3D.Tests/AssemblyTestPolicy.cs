// Parallelism and fixture lifetime are DELIBERATELY not set for this assembly, and this file
// exists to say so rather than leaving the absence looking like an oversight.
//
// **The rule is per test-KIND, not per repo**, and applying either half everywhere is wrong:
//
//   - **Unit and integration assemblies** get `[assembly: Parallelizable]` together with
//     `[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]`. The isolation is what makes the
//     parallelism safe, so the two travel together - and per-test construction is the xUnit
//     behaviour those projects are migrating from, which was right for them.
//   - **UI assemblies get neither.** A UI fixture holds a launched application and an attached
//     driver; sharing one instance across its tests is the point, because that setup is the
//     expensive part. And in-process parallelism there is not slow, it is unsafe: UI tests drive
//     a single desktop, and a second run stealing focus mid-click delivers it into whatever is
//     now in front. UI tests parallelise across CI MATRIX LEGS - separate machines, each with its
//     own desktop - never across threads.
//
// **This assembly is the one that will grow UI tests**, so it takes the UI side of that split.
// Today's four tests construct forms without showing them, so they are fast and isolated anyway
// and gain nothing measurable from parallelism; the setting is absent because of what comes next,
// not because of what is here now.
//
// A fixture that genuinely needs per-test isolation can carry the attribute itself, which keeps
// the exception beside the code it affects.
