using System.Diagnostics;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The per-part means printed beside the rate line.</summary>
public sealed class FramePartsTests
{
    private static readonly long Millisecond = Stopwatch.Frequency / 1000;

    /// <remarks>Two frames, parts of 2+4 and 6 ms: the means are 3 and 3, in first-seen order — not the last frame's.</remarks>
    [Test]
    public void Report_OverTwoFrames_PrintsEachPartsMeanInOrder()
    {
        FrameParts parts = new();

        parts.Add("pose", 2 * Millisecond);
        parts.Add("particles", 6 * Millisecond);
        parts.Frame();
        parts.Add("pose", 4 * Millisecond);
        parts.Frame();

        parts.Report().ShouldBe("pose 3, particles 3");
    }

    [Test]
    public void Report_AfterReporting_StartsANewWindow()
    {
        FrameParts parts = new();
        parts.Add("pose", 9 * Millisecond);
        parts.Frame();
        _ = parts.Report();

        parts.Report().ShouldBeEmpty();
    }
}
