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
using System;

[assembly: Parallelizable(ParallelScope.All)]
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>Picks OpenAL's null backend before any test in this assembly opens a device.</summary>
/// <remarks>
/// **Per ASSEMBLY, because OpenAL reads `ALSOFT_DRIVERS` once per PROCESS.** The driver list is read
/// when the library initialises, so the first `alcOpenDevice` anywhere in the run decides what every
/// later one gets, and a variable set after that is ignored.
///
/// **A `[OneTimeSetUp]` on one fixture cannot do this job**, and the attempt is what the owner
/// heard: *"umm should i be hearing stuff from the audio test?"*. `AudioOutputDeviceTests` set the
/// variable in its own `[OneTimeSetUp]` and said in a comment exactly what would go wrong without
/// it — but `AudioOutputMixTests` calls `AudioOutput.TryCreate()` with no setup of its own, and the
/// assembly is `ParallelScope.All`, so whichever fixture reached OpenAL first chose the backend. Run
/// in that order, the suite drove the developer's real sound card and played a tone.
///
/// A <see cref="SetUpFixtureAttribute"/> runs before every fixture in its namespace and below, which
/// is every test in this assembly, so there is no order left for the race to exploit. It would cover
/// the assembly regardless of namespace if it had none at all, but a namespace-less type is refused
/// here by CA1050 and S3903, and a test living outside `Tf2DemoSalvage.Audio.Tests` would be the
/// anomaly worth catching anyway.
/// </remarks>
[SetUpFixture]
public sealed class AudioDriverPolicy
{
    /// <summary>Selects the null backend.</summary>
    [OneTimeSetUp]
    public void UseTheNullDevice() =>
        Environment.SetEnvironmentVariable("ALSOFT_DRIVERS", "null");
}
