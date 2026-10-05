using Tf2DemoSalvage.Probe.Probes;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The footstep gate on real flags: every player in a nine-bit demo carries `FL_CLIENT` at `1&lt;&lt;7`, which the current
/// layout calls `FL_ATCONTROLS`. Read through <see cref="StepGateProbe.Measure"/>, the same walk the probe reports.
/// </summary>
/// <remarks>
/// Measured 2026-10-05 before the fix: 0 steps of 7,741 moving samples (2009 POV) and of 3,314 (2008 STV), against 963 of
/// 1,499 on the 2013 POV control.
/// </remarks>
public sealed class CorpusFootstepLayoutTests
{
    [TestCase("tf2-2009-build3862-pov-cp_badlands", TestName = "Step_The2009Specimen_Sounds")]
    [TestCase("tf2-2008-build3420-stv-cp_granary", TestName = "Step_The2008SourceTvSpecimen_Sounds")]
    [TestCase("tf2-2013-build1729296-pov-cp_badlands", TestName = "Step_The2013Specimen_Sounds")]
    public void Step_ByEra(string demo)
    {
        StepGateCounts counts = StepGateProbe.Measure(TimelineCache.For(Corpus.Demo(demo)), every: 3);

        counts.Moving.ShouldBeGreaterThan(0, "the control: players move");
        counts.Steps.ShouldBeGreaterThan(0);
    }
}
