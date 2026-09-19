using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>How far the timeline's decode has got, for the viewer's loading screen.</summary>
/// <remarks>
/// **The owner waits 80 seconds on a 26-minute demo with nothing on screen**, and asked for a progress bar. The decode walks the
/// demo's commands in order, so the fraction walked is the measure; this asserts its shape — rising, and ending at exactly one.
/// </remarks>
public sealed class DemoTimelineProgressTests
{
    [Test]
    public void Build_WithAProgressCallback_ReportsFractionsRisingToOne()
    {
        byte[] demo = SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            [
                SyntheticDemo.DataTables(new Core.Schema.DemoSchema([], [])),
                .. Enumerable.Range(0, 40).Select(tick => SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, tick)),
            ]);
        List<double> seen = [];

        DemoTimeline.Build(demo, seen.Add);

        seen.Count.ShouldBeGreaterThan(2, "reported along the way, not only at the end");
        seen.ShouldBe([.. seen.Order()], "never goes backwards");
        seen[0].ShouldBeLessThan(0.5d, "the first report comes early");
        seen[^1].ShouldBe(1d, "the last is complete");
    }
}
