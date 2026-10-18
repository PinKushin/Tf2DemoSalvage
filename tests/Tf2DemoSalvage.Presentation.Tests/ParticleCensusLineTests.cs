using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The census line `--measure` prints once a second, which a leak shows in as a count that climbs.</summary>
public sealed class ParticleCensusLineTests
{
    /// <remarks>Totals summed, names largest first, each as live/unoffered — distinct numbers so no column can stand in for another.</remarks>
    [Test]
    public void Format_ThreeNames_SumsAndListsThemLargestFirst()
    {
        string line = ParticleCensusLine.Format(
            30123.4,
            [("medicgun_beam_red", 5, 4, 0), ("ExplosionCore_Wall", 2, 1, 0), ("muzzle", 9, 0, 0), ("rocketjump_smoke", 0, 0, 3)]);

        line.ShouldBe(
            "census at tick 30123: bursts 16 (5 unoffered, 3 finished); muzzle 9/0; medicgun_beam_red 5/4; ExplosionCore_Wall 2/1");
    }
}
