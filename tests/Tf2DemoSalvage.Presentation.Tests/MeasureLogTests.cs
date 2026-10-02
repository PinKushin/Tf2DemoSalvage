using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// What <c>--measure</c> counts as a sample. The playback check read one number, "N samples", that was the rate reports PLUS
/// the rebuild-cost lines printed under some of them, so a run with few rebuilds failed a threshold meant for rate reports.
/// </summary>
public sealed class MeasureLogTests
{
    [Test]
    public void Samples_CountsRateReportsOnly_NotRebuildCosts()
    {
        MeasureLog log = new();

        log.AddRate("frame rate 1");
        log.AddRate("frame rate 2");
        log.AddRate("frame rate 3");
        log.AddCost("moment cost a");
        log.AddCost("moment cost b");

        Assert.Multiple(() =>
        {
            Assert.That(log.Samples, Is.EqualTo(3));
            Assert.That(log.Rebuilds, Is.EqualTo(2));
        });
    }

    [Test]
    public void Lines_DropsTheFirstRateReportAndItsCost_KeepingTheRest()
    {
        MeasureLog log = new();

        log.AddRate("r1");
        log.AddCost("c1");
        log.AddRate("r2");

        Assert.That(log.Lines, Is.EqualTo(new[] { "  r2" }));
    }

    [Test]
    public void Clear_ResetsBothCounts()
    {
        MeasureLog log = new();
        log.AddRate("r");
        log.AddCost("c");

        log.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(log.Samples, Is.EqualTo(0));
            Assert.That(log.Rebuilds, Is.EqualTo(0));
            Assert.That(log.Lines, Is.Empty);
        });
    }
}
