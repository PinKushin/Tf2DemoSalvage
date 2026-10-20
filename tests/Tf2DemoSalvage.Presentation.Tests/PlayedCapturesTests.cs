using System.IO;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

[TestFixture]
public sealed class PlayedCapturesTests
{
    [Test]
    public void Due_FramesPlayedPastTheListedTicks_CaptureEachOnceOnTheFirstFrameAtOrPast()
    {
        PlayedCaptures captures = PlayedCaptures.Parse("out 120 100")!;

        (captures.Due(99.6), captures.Due(100.1), captures.Due(100.4), captures.Due(130d), captures.Due(131d))
            .ShouldBe((null, Path.Combine("out", "play-100.png"), null, Path.Combine("out", "play-120.png"), null));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("out")]
    [TestCase("out x")]
    public void Parse_NoTick_IsNull(string? value) => PlayedCaptures.Parse(value).ShouldBeNull();

    [Test]
    public void Seconds_AfterRestartAndAfterReset_IsTheStepThenZero()
    {
        FixedStepTime time = FixedStepTime.Parse("0.0125")!;

        double before = time.Seconds;
        time.Restart();
        double running = time.Seconds;
        time.Restart();
        double again = time.Seconds;
        time.Reset();

        (before, running, again, time.Seconds).ShouldBe((0d, 0.0125d, 0.0125d, 0d));
    }

    [TestCase(null)]
    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("fast")]
    public void Parse_NotAPositiveStep_IsNull(string? value) => FixedStepTime.Parse(value).ShouldBeNull();
}
