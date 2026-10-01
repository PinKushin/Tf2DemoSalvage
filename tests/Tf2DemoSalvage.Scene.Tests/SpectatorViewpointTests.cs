using System.Collections.Generic;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The point-of-view camera is <c>InterpolateViewpoint</c>'s view at the drawn frame's fractional tick (B56).
/// </summary>
/// <remarks>
/// <c>CViewRender</c> builds the view from the local player that <c>InterpolateViewpoint</c> set this frame, so the
/// camera asks the eye source for its viewpoint at the moment being drawn — never for the packet at the whole tick.
/// The stand-in answers the two questions differently so a camera reading the wrong one cannot pass.
/// </remarks>
public sealed class SpectatorViewpointTests
{
    [Test]
    public void Eye_BetweenTwoPackets_IsTheViewpointAtTheFractionalTick()
    {
        FreeCamera camera = View().Eye(105.5, 1f).ShouldNotBeNull();

        camera.Origin.X.ShouldBe(1055f, 1e-3f);
        camera.Angles.Yaw.ShouldBe(52.75f, 1e-3f);
    }

    [Test]
    public void Chase_OnAPointOfViewDemo_IsTheSameViewpoint()
    {
        // A POV demo's camera is the recorded one whatever the mode (D128, B417), so the chase asks the same question.
        View().Chase(105.5, 1f).ShouldNotBeNull().Origin.X.ShouldBe(1055f, 1e-3f);
    }

    private static SpectatorView View() => new(new RecordingLogger()) { Eyes = new Interpolating() };

    /// <summary>A point-of-view demo whose interpolated view moves ten units a tick and whose packets do not.</summary>
    private sealed class Interpolating : IEyeSource
    {
        public bool HasRecordedView => true;

        public int? RecorderEntityIndex => 1;

        public RecordedView? RecordedViewAt(int tick) => new RecordedView { ViewOrigin = (0f, 0f, 0f) };

        public RecordedView? ViewpointAt(double tick) =>
            new RecordedView { ViewOrigin = ((float)tick * 10f, 0f, 0f), ViewAngles = (0f, (float)tick / 2f, 0f) };

        public IReadOnlyList<ScenePlayer> PlayersAt(int tick) =>
            [new ScenePlayer(1, 0f, 0f, 0f, SceneTeams.Red, 125, 3)];
    }
}
