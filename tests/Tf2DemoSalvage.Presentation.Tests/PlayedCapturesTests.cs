using System.IO;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

[TestFixture]
public sealed class PlayedCapturesTests
{
    [Test]
    public void Due_FramesPlayedPastTheListedTicks_CaptureEachOnceOnTheFirstFrameAtOrPast()
    {
        string folder = Folder();
        PlayedCaptures captures = PlayedCaptures.Parse($"{folder} 120 100 140", out _)!;

        (captures.Due(99.6), captures.Due(100.1), captures.Due(100.4), captures.Due(120d), captures.Due(139.9), captures.Due(150d))
            .ShouldBe((null, Path.Combine(folder, "play-100.png"), null, Path.Combine(folder, "play-120.png"), null, Path.Combine(folder, "play-140.png")));
    }

    /// <remarks>
    /// The device writes a capture on the NEXT present (`Device3D`), the frame after it is asked for, so the sidecar is
    /// owed on the frame after that capture: then the state it reads is the frame the PNG shows (review 2, finding 1).
    /// </remarks>
    [Test]
    public void SidecarDue_TheFrameAfterACapture_IsThatCaptureAndOnlyOnce()
    {
        PlayedCaptures captures = PlayedCaptures.Parse($"{Folder()} 100", out _)!;

        // Each frame asks for the owed sidecar first, then for a capture.
        string? first = captures.SidecarDue();
        string shot = captures.Due(100.2)!;
        string? second = captures.SidecarDue();
        _ = captures.Due(101d);
        string? third = captures.SidecarDue();

        (first, second, third).ShouldBe((null, shot, null));
    }

    [Test]
    public void Parse_AFolderThatDoesNotExist_CreatesIt()
    {
        string folder = Path.Combine(Folder(), "fresh", "run-3");

        PlayedCaptures.Parse($"{folder} 100", out _).ShouldNotBeNull();

        Directory.Exists(folder).ShouldBeTrue();
    }

    [TestCase(null, null)]
    [TestCase("", null)]
    [TestCase("out", null)]
    public void Parse_NoTick_IsNullAndNotAProblem(string? value, string? problem)
    {
        PlayedCaptures.Parse(value, out string? said).ShouldBeNull();
        said.ShouldBe(problem);
    }

    [TestCase("out x")]
    [TestCase("out 30,100")]
    [TestCase("out 30100.5")]
    [TestCase("out -5")]
    [TestCase("D:\\pair shots 30100")]
    public void Parse_ATokenThatIsNotATick_IsNullAndSaysWhich(string value)
    {
        PlayedCaptures.Parse(value, out string? said).ShouldBeNull();
        said.ShouldNotBeNull();
    }

    private static string Folder() => Path.Combine(Path.GetTempPath(), "played-captures-" + System.Guid.NewGuid().ToString("N"));

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
