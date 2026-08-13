using System;
using System.IO;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// The playback controls, driven the way a person drives them.
/// </summary>
/// <remarks>
/// **Laid out like a video player** — jump to start, shuttle down, play, shuttle up, jump to end,
/// then the scrub bar and the readouts. That is what someone reaching for it expects, and the
/// shuttle ladder runs through reverse because this viewer can do something the engine cannot:
/// TF2 streams a demo forward and each snapshot is a delta on the last, so it has nothing to step
/// back into.
/// </remarks>
[TestFixture]
public sealed class TransportUiTests
{
    private ViewerApplication _viewer = null!;

    private static string DemoPath => Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "..", "..", "..", "..", "..",
        "tools", "corpus", "demos", "tf2-2013-build1729296-pov-cp_badlands.dem"));

    [OneTimeSetUp]
    public void LaunchViewerWithADemo()
    {
        if (!File.Exists(DemoPath))
        {
            Assert.Ignore($"the corpus demo is not present at {DemoPath}");
            return;
        }

        _viewer = ViewerApplication.Launch(DemoPath);

        Retry.WhileFalse(
            () => _viewer.Exists(TransportBar.ScrubBarId),
            TimeSpan.FromSeconds(60),
            throwOnTimeout: true,
            timeoutMessage: "The transport never appeared.");
    }

    [OneTimeTearDown]
    public void CloseViewer() => _viewer?.Dispose();

    [Test]
    public void TheSpeedReadoutFollowsTheShuttleButtonsIntoReverse()
    {
        // **The readout is the point.** A speed that changes with nothing to show it leaves the
        // user guessing whether the button did anything, and reverse especially needs saying: a
        // demo running backwards at a quarter speed looks a lot like one that has stalled.
        AutomationElement speed = _viewer.Find(TransportBar.SpeedLabelId);

        speed.Name.ShouldBe("speed 1x", "a freshly opened demo plays at real time");

        _viewer.Find(TransportBar.FasterButtonId).AsButton().Invoke();

        Retry.WhileFalse(
            () => _viewer.Find(TransportBar.SpeedLabelId).Name == "speed 2x",
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "The speed readout did not follow the faster button.");

        // Down past one, past the quarter speeds, and into reverse. The ladder is
        // -4 -2 -1 -0.5 -0.25 0.25 0.5 1 2 4 8, so from 2x that is five steps to -0.25x.
        for (int step = 0; step < 5; step++)
        {
            _viewer.Find(TransportBar.SlowerButtonId).AsButton().Invoke();
        }

        Retry.WhileFalse(
            () => _viewer.Find(TransportBar.SpeedLabelId).Name.Contains(
                "reversed", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "The shuttle never reached reverse.");

        TestContext.Out.WriteLine(
            $"TRANSPORT speed reads '{_viewer.Find(TransportBar.SpeedLabelId).Name}'");
    }

    [Test]
    public void JumpingToTheEndMovesTheScrubBar()
    {
        // A jump is a seek and must be heard as one, unlike playback reporting where it has got
        // to - the two go through different paths in the bar for exactly that reason.
        AutomationElement scrub = _viewer.Find(TransportBar.ScrubBarId);

        double before = scrub.Patterns.RangeValue.Pattern.Value;

        _viewer.Find(TransportBar.EndButtonId).AsButton().Invoke();

        Retry.WhileFalse(
            () => _viewer.Find(TransportBar.ScrubBarId).Patterns.RangeValue.Pattern.Value > before,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "Jumping to the end did not move the scrub bar.");

        _viewer.Find(TransportBar.StartButtonId).AsButton().Invoke();

        Retry.WhileFalse(
            () => _viewer.Find(TransportBar.ScrubBarId).Patterns.RangeValue.Pattern.Value == 0,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "Jumping to the start did not return to tick zero.");
    }
}
