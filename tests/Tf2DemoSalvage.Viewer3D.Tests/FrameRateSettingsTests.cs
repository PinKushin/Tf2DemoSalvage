using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The frame rate limit and vertical sync settings.
/// </summary>
/// <remarks>
/// **Both exist because the measurement contradicted the code.** The swap chain presents with a
/// sync interval of one, which asks for vsync — and the viewer was measured at about 600 frames a
/// second on a machine whose driver forces vsync off globally. A driver override outranks the
/// present call, so the only honest cap is one this program applies itself.
///
/// 300 is Source's own <c>fps_max</c> ceiling. The lower values matter to whoever is recording:
/// 24 for film cadence, 30 and 60 for ordinary video.
/// </remarks>
public sealed class FrameRateSettingsTests
{
    [Test]
    public void ByDefault_TheRateIsCappedWhereSourceCapsIt()
    {
        // Not uncapped. At 600 frames a second the viewer does ten times the work for a display
        // that cannot show it, and every one of those frames allocates.
        new ViewerSettings().FrameRateLimit.ShouldBe(300);
    }

    [Test]
    public void ByDefault_VerticalSyncIsOff()
    {
        // The owner turns it off globally and dislikes the latency. A default that fights the
        // machine's own setting is a default that produces support questions.
        new ViewerSettings().VerticalSync.ShouldBeFalse();
    }

    [Test]
    [TestCase(24)]
    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    [TestCase(300)]
    public void ARateAVideoMakerWouldWant_SurvivesAWriteAndRead(int rate)
    {
        // The round trip is the test: a setting that writes but does not read back is a setting
        // that silently reverts on the next launch.
        ViewerSettings written = new() { FrameRateLimit = rate, VerticalSync = true };

        ViewerSettings read = ViewerSettings.Parse(written.Write());

        read.FrameRateLimit.ShouldBe(rate);
        read.VerticalSync.ShouldBeTrue();
    }

    [Test]
    public void AnUncappedRate_IsAllowedAndMeansNoWaiting()
    {
        // Zero is uncapped, for benchmarking. Kept expressible rather than clamped away, because
        // measuring how fast the renderer CAN go is a real question - it is how the 600 was found.
        ViewerSettings read = ViewerSettings.Parse(new ViewerSettings { FrameRateLimit = 0 }.Write());

        read.FrameRateLimit.ShouldBe(0);
    }

    [Test]
    public void ANegativeRate_IsIgnoredRatherThanObeyed()
    {
        // A hand-edited file can say anything. A negative budget would make every frame overdue
        // and the cap a no-op, which looks exactly like the cap not working.
        ViewerSettings.Parse("frame_rate_limit -60").FrameRateLimit.ShouldBe(300);
    }
}
