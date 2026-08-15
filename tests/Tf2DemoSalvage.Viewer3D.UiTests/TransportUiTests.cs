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
        // **Derived from the bar's own ladder, not typed out.** Both numbers below were hardcoded
        // against a wording the constructor used and no update ever produced, so the test failed on
        // the first press of faster and the failure read as a broken transport bar. Asking
        // TransportBar.Speeds means a change to the ladder or to the wording moves the test with it
        // instead of breaking it.
        AutomationElement speed = _viewer.Find(TransportBar.SpeedLabelId);

        speed.Name.ShouldBe(
            TransportBar.SpeedDescription(1), "a freshly opened demo plays at real time");

        _viewer.Find(TransportBar.FasterButtonId).AsButton().Invoke();

        Retry.WhileFalse(
            () => _viewer.Find(TransportBar.SpeedLabelId).Name == TransportBar.SpeedDescription(2),
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "The speed readout did not follow the faster button.");

        // Down past one, past the quarter speeds, and into reverse. The ladder is
        // -4 -2 -1 -0.5 -0.25 0.25 0.5 1 2 4 8, so from 2x that is four steps to -0.25x and five to
        // -0.5x. It said five to -0.25x, which nothing caught because the assertion below only asks
        // whether the wording says reversed.
        for (int step = 0; step < 5; step++)
        {
            _viewer.Find(TransportBar.SlowerButtonId).AsButton().Invoke();
        }

        // **The exact speed, not merely "something reversed".** Contains("reversed") is true for
        // every one of the five reverse rungs, so it cannot tell a shuttle that stepped correctly
        // from one that ran to the end of the ladder — and it is why the off-by-one above sat in
        // the comment unnoticed.
        Retry.WhileFalse(
            () => _viewer.Find(TransportBar.SpeedLabelId).Name ==
                TransportBar.SpeedDescription(-0.5),
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "The shuttle never reached −0.5x, five steps down from 2x.");

        TestContext.Out.WriteLine(
            $"TRANSPORT speed reads '{_viewer.Find(TransportBar.SpeedLabelId).Name}'");
    }

    [Test]
    public void JumpingToTheEndMovesTheScrubBar()
    {
        // A jump is a seek and must be heard as one, unlike playback reporting where it has got
        // to - the two go through different paths in the bar for exactly that reason.
        // **Read through the tick readout, not the scrub bar's RangeValue pattern.** That pattern
        // is not supported on this control — "The requested pattern 'RangeValue' is not supported",
        // with a null native pattern underneath — so the previous version of this test could never
        // have passed against any behaviour. It threw in 43 ms, before the button was ever pressed,
        // which is worth recognising: a test that fails faster than the thing it measures could
        // possibly happen is failing at the instrument, not at the application.
        //
        // The readout is also the better measurement. It is what the person watching actually sees,
        // and it says which tick rather than only that a slider moved.
        string StartTick() => _viewer.Find(TransportBar.TickLabelId).Name;

        string before = StartTick();

        _viewer.Find(TransportBar.EndButtonId).AsButton().Invoke();

        Retry.WhileFalse(
            () => StartTick() != before,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: $"Jumping to the end did not move playback; the tick still reads {before}.");

        string atEnd = StartTick();

        _viewer.Find(TransportBar.StartButtonId).AsButton().Invoke();

        Retry.WhileFalse(
            () => StartTick() != atEnd,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: $"Jumping to the start did not move playback; the tick still reads {atEnd}.");

        // **Stated rather than left to the retry.** Waiting for a condition and asserting one are
        // different things: the retry says when to stop looking, and this says what the answer had
        // to be. An analyser reads only the second, and so does anyone deciding what this test
        // claims - the timeout message is not a prediction, it is an excuse prepared in advance.
        // Back to exactly where the demo opened, which is the first tick — asserted against the
        // reading taken before anything was pressed rather than against a formatted string, so it
        // stays true whatever the readout's wording is and whatever tick a demo starts on. Demo
        // ticks do not begin at zero, so a literal 0 here would be wrong for most files.
        StartTick().ShouldBe(before, "the start button seeks back to the demo's first tick");
    }
}
