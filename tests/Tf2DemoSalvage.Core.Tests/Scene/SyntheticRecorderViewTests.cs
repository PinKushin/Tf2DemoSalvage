using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The recorder's body where <c>InterpolateViewpoint</c> put him, animated from his own view (B56, B442).
/// </summary>
/// <remarks>
/// <c>IPrediction::SetViewOrigin</c> is his origin (<c>prediction.cpp:1837-1847</c>) and <c>SetLocalViewAngles</c> his
/// <c>pl.v_angle</c> (<c>:1901-1908</c>). <c>C_TFPlayer::UpdateClientSideAnimation</c> animates the local player from
/// <c>EyeAngles()</c> (<c>c_tf_player.cpp:4279-4284</c>), which for him is <c>pl.v_angle</c>
/// (<c>:7280-7290</c>, <c>baseplayer_shared.cpp:303-311</c>), and his velocity is <c>GetAbsVelocity()</c>
/// (<c>c_baseentity.cpp:5852-5858</c>) — the last <c>m_vecVelocity</c> received, with no interpolation history
/// (<c>c_baseentity.cpp:907-912</c> leaves its <c>AddVar</c> commented out). Everyone else is unchanged.
/// </remarks>
public sealed class SyntheticRecorderViewTests
{
    /// <summary>Where the demo player put him this frame, looking along +Y and down.</summary>
    private static readonly RecordedView Viewpoint = new()
    {
        ViewOrigin = (1000f, 2000f, 30f),
        ViewAngles = (-20f, 90f, 0f),
        LocalViewAngles = (-20f, 90f, 0f),
    };

    [Test]
    public void PlayersAt_WithAViewpoint_PutsTheRecorderAtItsOrigin()
    {
        ScenePlayer recorder = Recorder(Viewpoint);

        (recorder.X, recorder.Y, recorder.Z).ShouldBe((1000f, 2000f, 30f));
    }

    [Test]
    public void PlayersAt_WithAViewpoint_AnimatesTheRecorderFromItsLocalAngles()
    {
        // The local angles, not the view angles and not the server's m_angEyeAngles (zero here).
        RecordedView view = Viewpoint with { ViewAngles = (10f, 45f, 0f) };

        ScenePlayer recorder = Recorder(view);

        recorder.EyeYaw.ShouldNotBeNull().ShouldBe(90f, 1e-3f);
        recorder.EyePitch.ShouldNotBeNull().ShouldBe(-20f, 1e-3f);
    }

    [Test]
    public void PlayersAt_WithAViewpoint_RunsTheRecorderOnHisNetworkedVelocity()
    {
        // (0, 300, 0) networked, along X differenced: GetOuterXYSpeed is 300, and running along his view (+Y) is
        // move_x 1, move_y 0 — the differenced route would be 267 and (0, 1).
        ScenePlayer recorder = Recorder(Viewpoint);

        recorder.Speed.ShouldBe(300f, 1e-3f);
        recorder.MoveX.ShouldBe(1f, 1e-3f);
        recorder.MoveY.ShouldBe(0f, 1e-3f);
    }

    [Test]
    public void PlayersAt_WithAPredictedVelocity_RunsTheRecorderOnItInsteadOfTheNetworkedOne()
    {
        // D205: prediction's m_vecVelocity, (300, 0, 0) — along X while he looks along +Y — replaces the networked
        // (0, 300, 0). Travel 0 against a body yaw of 90 is a strafe: move_x 0, move_y 1.
        List<ScenePlayer> players = [];
        DemoTimeline.Build(SyntheticPlayer.DemoOfARecorderAndABystander())
            .PlayersAt(105.5, players, true, Viewpoint, (300f, 0f, 0f));

        ScenePlayer recorder = players.Single(player => player.EntityIndex == 1);

        recorder.Speed.ShouldBe(300f, 1e-3f);
        recorder.MoveX.ShouldBe(0f, 1e-3f);
        recorder.MoveY.ShouldBe(1f, 1e-3f);
    }

    [Test]
    public void PlayersAt_WithAViewpoint_TwistsTheTorsoFromTheFeetToTheLocalYaw()
    {
        // body_yaw = -(eyeYaw - currentFeetYaw): the feet the timeline advanced (zero, along his server eye yaw)
        // against his local yaw of 90. Within one step of the fixture's 12-bit eye yaw (360 / 4096 = 0.088), which
        // cannot say zero exactly and leaves the feet at 0.044.
        Recorder(Viewpoint).AimYaw.ShouldNotBeNull().ShouldBe(-90f, 0.088f);
    }

    [Test]
    public void PlayersAt_WithAViewpoint_LeavesEveryoneElseAsTheyWere()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfARecorderAndABystander());

        List<ScenePlayer> without = [];
        List<ScenePlayer> with = [];

        timeline.PlayersAt(105.5, without);
        timeline.PlayersAt(105.5, with, true, Viewpoint);

        with.Single(player => player.EntityIndex == 2).ShouldBe(without.Single(player => player.EntityIndex == 2));
    }

    [Test]
    public void PlayersAt_WithoutAViewpoint_LeavesTheRecorderOnHisTrack()
    {
        // The control: no viewpoint (a SourceTV demo, or the default democmdinfo_t), so he is drawn like anyone.
        List<ScenePlayer> players = [];
        DemoTimeline.Build(SyntheticPlayer.DemoOfARecorderAndABystander()).PlayersAt(105.5, players);

        players.Single(player => player.EntityIndex == 1).Y.ShouldBe(100f, 1e-3f);
    }

    private static ScenePlayer Recorder(RecordedView viewpoint)
    {
        List<ScenePlayer> players = [];
        DemoTimeline.Build(SyntheticPlayer.DemoOfARecorderAndABystander()).PlayersAt(105.5, players, true, viewpoint);

        return players.Single(player => player.EntityIndex == 1);
    }
}
