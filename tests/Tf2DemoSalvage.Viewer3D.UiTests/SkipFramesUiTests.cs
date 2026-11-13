using System.Globalization;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>The opening seek on the session's demo reads in the engine's batches, drawing as it goes (B708).</summary>
public sealed class SkipFramesUiTests
{
    private static readonly Regex Landed = new(
        Regex.Escape(FramePresenter.SkipLandedLine) + @" (?<tick>\d+) after (?<batches>\d+) batches over (?<frames>\d+) frames, at most (?<most>\d+) in a frame",
        RegexOptions.CultureInvariant,
        System.TimeSpan.FromSeconds(1));

    /// <remarks>
    /// `ReadPacket` caps a read loop at 99 packets and `_Host_RunFrame` renders after its loops, one per tick accumulated and at most
    /// `Host_FilterTime`'s 0.1 s of them. So a walk from the start to the opening tick takes about tick / 99 batches, over more
    /// than one drawn frame, and never more batches in a frame than the cap allows. Counts carried from the walk's own line.
    /// </remarks>
    [Test]
    public void OpeningSeek_OnTheSessionsDemo_ReadsInBatchesOverDrawnFrames()
    {
        // The FIRST landing, which is the opening seek's: the session is shared, and a test that ran earlier and seeks lands later
        // (the last one read 19997 on 2026-11-12: a later skip in the session, not the opening one).
        string line = ViewerSession.App.FirstLine(FramePresenter.SkipLandedLine).ShouldNotBeNull("the opening seek never landed");
        Match landed = Landed.Match(line);

        landed.Success.ShouldBeTrue(line);
        int.Parse(landed.Groups["tick"].Value, CultureInfo.InvariantCulture).ShouldBe(ViewerSession.OpeningTick);

        int batches = int.Parse(landed.Groups["batches"].Value, CultureInfo.InvariantCulture);
        int frames = int.Parse(landed.Groups["frames"].Value, CultureInfo.InvariantCulture);

        if (batches <= 1)
        {
            Assert.Ignore($"This machine has no sound scripts, so the seek read no frames: {line}");
            return;
        }

        // One batch per 99 ticks, and the empty one that ends them; the range is the demo's first tick, which is not zero.
        (batches - 1).ShouldBeInRange(
            (ViewerSession.OpeningTick / DemoSkip.PacketsPerFrame) - 1,
            ViewerSession.OpeningTick / DemoSkip.PacketsPerFrame);
        frames.ShouldBeGreaterThan(1, "the frames are drawn, not run in one call");
        int.Parse(landed.Groups["most"].Value, CultureInfo.InvariantCulture)
            .ShouldBeLessThanOrEqualTo(DemoSkip.BatchesThisFrame(double.MaxValue, 0.015f), "no frame reads more than `Host_FilterTime`'s 0.1 s of batches");
    }
}
