using System.IO;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A real point-of-view demo's camera, between two packets, through the production chain (B56).
/// </summary>
/// <remarks>
/// **The output-level assertion** (<c>docs/memory/output-level-assertion-or-it-is-not-done.md</c>): the camera
/// <c>SpectatorView.Eye</c> builds from a <c>TimelineEyes</c> over a <see cref="DemoPlayer"/>, at a tick half way
/// between two packets, sits half way between their recorded origins — not on the older packet, which is what the
/// stepped view drew. Tick 5541 is B442's: the recorder running in a straight line, mid-demo.
/// </remarks>
public sealed class CorpusPovViewInterpolationTests
{
    private const string MovementDemo = "movement-test-pov-cp_process";

    private const int Tick = 5541;

    [Test]
    public void Eye_HalfwayBetweenTwoPackets_IsHalfwayBetweenTheirRecordedOrigins()
    {
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(Corpus.Demo(MovementDemo)));
        RecordedView before = timeline.RecordedViewAt(Tick).ShouldNotBeNull();
        RecordedView after = timeline.RecordedViewAt(Tick + 1).ShouldNotBeNull();

        // The control: the two packets are apart, so "half way" and "the older one" are different answers.
        (after.Origin.X - before.Origin.X).ShouldNotBe(0f);

        SpectatorView view = new(NullLogger.Instance) { Eyes = new TimelineEyes(timeline, new DemoPlayer(timeline)) };

        FreeCamera camera = view.Eye(Tick + 0.5, 1f).ShouldNotBeNull();

        camera.Origin.X.ShouldBe((before.Origin.X + after.Origin.X) / 2f, 0.01f);
        camera.Origin.Y.ShouldBe((before.Origin.Y + after.Origin.Y) / 2f, 0.01f);
    }
}
