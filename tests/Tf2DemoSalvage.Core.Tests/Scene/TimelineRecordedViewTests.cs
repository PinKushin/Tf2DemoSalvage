using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The recorded camera, reachable from the timeline by tick.
/// </summary>
/// <remarks>
/// **The viewer needs the view at whatever tick is being drawn, and the demo stores it per packet.**
/// The timeline already walks every command to build its frames, so it is the one place that can
/// answer without a second pass over the file — a 39 MB demo re-walked per frame would be the whole
/// cost of drawing.
///
/// Written against a synthetic demo, because the question here is retrieval rather than decoding:
/// the values are chosen so a lookup that returned the wrong packet's view is a different number
/// rather than a plausible one. What the bytes MEAN was settled separately and on real files —
/// <c>docs/findings/01-container.md</c>.
/// </remarks>
public sealed class TimelineRecordedViewTests
{
    [Test]
    public void RecordedViewAt_ATickWithAPacket_IsThatPacketsView()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithRecordedViews(
            (100, (10f, 20f, 30f)),
            (200, (40f, 50f, 60f))));

        timeline.RecordedViewAt(100).ShouldNotBeNull().Origin.ShouldBe((10f, 20f, 30f));
        timeline.RecordedViewAt(200).ShouldNotBeNull().Origin.ShouldBe((40f, 50f, 60f));
    }

    [Test]
    public void RecordedViewAt_ATickBetweenPackets_IsTheMostRecentOne()
    {
        // **The demo speaks at ticks and the viewer draws between them**, so the answer for tick
        // 150 is what the camera was last told, not nothing. Returning null between packets would
        // make the view flicker back to another camera on most frames.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithRecordedViews(
            (100, (10f, 20f, 30f)),
            (200, (40f, 50f, 60f))));

        timeline.RecordedViewAt(150).ShouldNotBeNull().Origin.ShouldBe((10f, 20f, 30f));

        // And not the later one, which is what a search that rounded to the nearest would give.
        timeline.RecordedViewAt(199).ShouldNotBeNull().Origin.ShouldBe((10f, 20f, 30f));
    }

    [Test]
    public void RecordedViewAt_ATickBeforeTheFirstPacket_IsNothing()
    {
        // Before the first packet there is no recorded camera, and inventing the first one would
        // place the view somewhere the recording never was.
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoWithRecordedViews((100, (10f, 20f, 30f))));

        timeline.RecordedViewAt(99).ShouldBeNull();
    }

    [Test]
    public void RecordedViewAt_ADemoWithNoRecordedView_IsNothingAtEveryTick()
    {
        // **A SourceTV demo leaves democmdinfo_t zeroed**, and a zeroed view is not a camera at
        // the world origin — it is the absence of one. Reporting (0,0,0) would put the viewer
        // inside the middle of the map and look like a camera bug rather than a missing feature.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithRecordedViews(
            (100, (0f, 0f, 0f)),
            (200, (0f, 0f, 0f))));

        timeline.RecordedViewAt(100).ShouldBeNull();
        timeline.RecordedViewAt(200).ShouldBeNull();
    }

    [Test]
    public void HasRecordedView_SaysWhetherAPointOfViewCameraIsAvailableAtAll()
    {
        // The viewer asks this once, to decide whether the mode can be offered — a per-frame null
        // check cannot tell "not yet" from "never".
        DemoTimeline pov = DemoTimeline.Build(
            SyntheticPlayer.DemoWithRecordedViews((100, (10f, 20f, 30f))));

        DemoTimeline sourceTv = DemoTimeline.Build(
            SyntheticPlayer.DemoWithRecordedViews((100, (0f, 0f, 0f))));

        pov.HasRecordedView.ShouldBeTrue();
        sourceTv.HasRecordedView.ShouldBeFalse();
    }
}
