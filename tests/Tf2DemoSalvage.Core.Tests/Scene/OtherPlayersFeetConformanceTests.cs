using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>Another player's feet and torso twist turn once a frame from his drawn eyes (B750).</summary>
/// <remarks>
/// <c>C_TFPlayer::UpdateClientSideAnimation</c> runs every frame and hands a non-local player's anim state
/// <c>m_angEyeAngles</c> (<c>c_tf_player.cpp:4287</c>), the member the interpolator writes (B749).
/// <c>ComputePoseParam_AimYaw</c> converges the feet by <c>gpGlobals-&gt;frametime</c>
/// (<c>multiplayer_animstate.cpp:1759</c>), draws the body at them (<c>m_angRender[YAW] = m_flCurrentFeetYaw</c>,
/// <c>:1765</c>), and twists by <c>-(m_flEyeYaw - m_flCurrentFeetYaw)</c> (<c>:1768-1772</c>).
///
/// The fixture's player stands still and turns his eyes from 0 to 30 over one update gap, under the 45 degree allowance,
/// so the feet stay planted at 0 and the whole turn is the torso's.
/// </remarks>
public sealed class OtherPlayersFeetConformanceTests
{
    private const int UpdateGap = 4;

    [Test]
    public void PlayersAt_AStillPlayerTurningHisEyesBetweenTicks_IsDrawnAtHisPlantedFeet()
    {
        (DemoTimeline timeline, double moment) = Turning();
        PlayerFeet feet = new();

        ScenePlayer player = Drawn(timeline, moment, feet);

        // Planted where the timeline's feet stand: the wire's 0, which its 12 bits say as 0.044.
        float planted = timeline.PlayersAt((int)moment).ShouldHaveSingleItem().Yaw;
        planted.ShouldBe(0f, 0.05f);
        player.EyeYaw.ShouldNotBeNull().ShouldBeGreaterThan(5f, "the control: his eyes are part way round");
        player.Yaw.ShouldBe(planted);
    }

    [Test]
    public void PlayersAt_AStillPlayerTurningHisEyesBetweenTicks_TwistsByTheDrawnEyes()
    {
        (DemoTimeline timeline, double moment) = Turning();

        ScenePlayer player = Drawn(timeline, moment, new PlayerFeet());

        // `SetPoseParameter( m_iAimYaw, -( m_flEyeYaw - m_flCurrentFeetYaw ) )` with the feet planted: the drawn eyes
        // less the feet, negated. The frame's own twist is against the arrived 30.
        player.AimYaw.ShouldNotBeNull().ShouldBe(-(player.EyeYaw.ShouldNotBeNull() - player.Yaw), 0.001f);
        player.AimYaw.ShouldNotBeNull().ShouldBeGreaterThan(-25f, "the control: not the arrived eyes' twist of -30");
    }

    [Test]
    public void PlayersAt_TheFeetPastTheAllowance_ConvergeByTheFrameTime()
    {
        // Eyes swung to 90 standing still: the goal steps 45 (`multiplayer_animstate.cpp:1736-1745`) and the feet
        // converge at 720 degrees a second scaled by `FADE_TURN_DEGREES` (`ConvergeYawAngles`), by each frame's time.
        // The oracle is `FeetYaw` itself, driven by hand with the same eyes and the same frame times — its own
        // conformance tests hold the arithmetic; this holds that the frame time is the frame's, not a tick's.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfEyesOverTicks(
            0.015f, (100, 0f, 0f, 0f), (104, 0f, 90f, 0f), (108, 0f, 90f, 0f), (140, 0f, 90f, 0f)));
        int delay = timeline.TrackFor(1).ShouldNotBeNull().InterpolationDelayTicks;
        PlayerFeet feet = new();
        double first = 108 + delay;
        double second = first + 0.25;

        ScenePlayer start = Drawn(timeline, first, feet);
        ScenePlayer next = Drawn(timeline, second, feet);

        FeetYaw expected = FeetYaw.Planted(start.Yaw);
        expected.Advance(next.EyeYaw.ShouldNotBeNull(), 0f, 0.25f * 0.015f);

        next.Yaw.ShouldBe(expected.Current, 0.0001f);
        next.Yaw.ShouldNotBe(start.Yaw, "the control: a quarter of a tick moved the feet");
    }

    [Test]
    public void PlayersAt_WithNoCarriedFeet_IsTheTimelinesFeet()
    {
        // The seek scan's side-effect-free read (D233) carries no feet: it answers the timeline's, not the eyes.
        (DemoTimeline timeline, double moment) = Turning();
        ScenePlayer stated = timeline.PlayersAt((int)moment).ShouldHaveSingleItem();

        timeline.PlayerAt(moment, 1).ShouldNotBeNull().Yaw.ShouldBe(stated.Yaw);
    }

    /// <summary>A still player turning his eyes 0 to 30 between ticks 100 and 104; the moment half way, as drawn.</summary>
    private static (DemoTimeline Timeline, double Moment) Turning()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfEyesOverTicks(
            0.015f, (100, 0f, 0f, 0f), (100 + UpdateGap, 0f, 30f, 0f), (100 + (2 * UpdateGap), 0f, 30f, 0f)));

        return (timeline, 100 + timeline.TrackFor(1).ShouldNotBeNull().InterpolationDelayTicks + (UpdateGap / 2d));
    }

    private static ScenePlayer Drawn(DemoTimeline timeline, double moment, PlayerFeet feet)
    {
        List<ScenePlayer> players = [];
        timeline.PlayersAt(moment, players, interpolating: true, viewpoint: null, recorderFeet: feet);

        return players.ShouldHaveSingleItem();
    }
}
