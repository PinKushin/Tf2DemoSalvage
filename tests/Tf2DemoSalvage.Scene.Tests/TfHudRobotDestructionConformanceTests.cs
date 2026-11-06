using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFHUDRobotDestruction` and its parts (game/client/tf/tf_hud_robot_destruction_status.cpp).</summary>
public sealed class TfHudRobotDestructionConformanceTests
{
    /// <remarks>:976-993: the minimum steal sits at the texture's dotted line, a quarter along; above it the rest of the bar.</remarks>
    [TestCase(0, 0f)]
    [TestCase(25, 0.25f)]
    [TestCase(162, 0.623636f)]
    [TestCase(300, 1f)]
    public void CarriedProgress_OnAsteroidsMaximum_MapsTheStealMinimumToAQuarter(int points, float progress) =>
        TfHudRobotDestruction.CarriedProgress(points, 300, 25).ShouldBe(progress, 1e-5f);

    /// <remarks>:1094: `tf_rd_points_per_approach` 5 over `tf_rd_points_approach_interval` 0.1, over rd_asteroid's 300 points.</remarks>
    [Test]
    public void ApproachSpeedFor_TheDefaultsOnAsteroid_IsASixthPerSecond() =>
        TfHudRobotDestruction.ApproachSpeedFor(5, 0.1f, 300).ShouldBe(50f / 300f, 1e-6f);

    /// <remarks>:896-905: distance from the far zone less its radius, over the zones' gap less both radii; BLU's flipped.</remarks>
    [TestCase(2, 500f, 0.5f)]
    [TestCase(2, 850f, 0.0625f)]
    [TestCase(3, 850f, 0.9375f)]
    [TestCase(2, -400f, 1f)]
    public void StolenProgress_BetweenZonesAThousandApart_IsValves(int team, float x, float progress)
    {
        SceneCaptureZone start = new(1, team, false, (0f, 0f, 0f)) { BoundingRadius = 100f };
        SceneCaptureZone end = new(2, 5 - team, false, (1000f, 0f, 0f)) { BoundingRadius = 100f };

        TfHudRobotDestruction.StolenProgress(team, (x, 0f, 0f), start, end).ShouldBe(progress, 1e-6f);
    }

    /// <remarks>:880-894: a dormant or disabled zone is passed over, so the earlier one of its side stands.</remarks>
    [TestCase(false, false, 11, 21)]
    [TestCase(true, false, 10, 21)]
    [TestCase(false, true, 10, 21)]
    public void Zones_ADormantOrDisabledLaterZone_IsPassedOver(bool dormant, bool disabled, int start, int end)
    {
        SceneCaptureZone[] zones =
        [
            new(10, 2, false, default), new(21, 3, false, default), new(11, 2, disabled, default) { Dormant = dormant },
        ];

        (SceneCaptureZone? s, SceneCaptureZone? e) = TfHudRobotDestruction.Zones(zones, 2);

        (s?.EntityIndex, e?.EntityIndex).ShouldBe((start, end));
    }

    [TestCase(1f, 0f, 0.3f, 0.3f)]
    [TestCase(0f, 0.5f, 0.3f, 0.2f)]
    [TestCase(0.5f, 0.4f, 0.3f, 0.5f)]
    public void Approach_TowardATarget_MovesAtMostTheSpeed(float target, float value, float speed, float expected) =>
        TfRobotDestructionProgressPanel.Approach(target, value, speed).ShouldBe(expected, 1e-6f);

    /// <remarks>:1177-1182: rising over the quarter second after a score, a pulse at the threshold, standard below it.</remarks>
    [TestCase(10f, 9.9f, 0f, 2f, 3f, 0.4f)]
    [TestCase(10f, 0f, 0.5f, 2f, 3f, 1f)]
    [TestCase(10f, 0f, 1f, 1f, 10f, 0.246817f)]
    public void ColorLerp_AfterAScoreOrAtTheThreshold_IsValves(float curTime, float lastScore, float progress, float threshold, float rate, float lerp) =>
        TfRobotDestructionProgressPanel.ColorLerp(curTime, lastScore, progress, threshold, rate).ShouldBe(lerp, 1e-5f);

    [Test]
    public void Tick_AtAQuarterPerSecond_ReachesHalfInTwoSecondsAndStops()
    {
        TfRobotDestructionProgressPanel bar = new(null, "BlueProgressBarFill") { ApproachSpeed = 0.25f };

        bar.SetProgress(0.5f, curTime: 0f);
        bar.Tick(1f);
        bar.CurrentProgress.ShouldBe(0.25f, 1e-6f);
        bar.Tick(2f);
        bar.CurrentProgress.ShouldBe(0.5f);
        bar.Tick(10f);
        bar.CurrentProgress.ShouldBe(0.5f);
    }

    [Test]
    public void RoundStart_AfterProgress_EmptiesTheBar()
    {
        TfRobotDestructionProgressPanel bar = new(null, "RedProgressBarFill");

        bar.SetProgress(0.75f, 0f, instant: true);
        bar.RoundStart();

        (bar.CurrentProgress, bar.EndProgress).ShouldBe((0f, 0f));
    }

    /// <remarks>:259: `%0.0f`, the CRT rounding a tie to even, and never below zero.</remarks>
    [TestCase(12.5f, 10f, "2")]
    [TestCase(13.5f, 10f, "4")]
    [TestCase(5f, 10f, "0")]
    public void RespawnText_ByTimeLeft_IsValves(float end, float now, string text) =>
        TfRobotIndicator.RespawnText(end, now).ShouldBe(text);

    /// <remarks>:195-212: only the local team's own group, only within two seconds of the attack.</remarks>
    [TestCase(9.5f, 2, 2, 129)]
    [TestCase(9.5f, 2, 3, 0)]
    [TestCase(7f, 2, 2, 0)]
    public void GlowAlpha_ByAttackAndTeam_IsValves(float lastAttacked, int groupTeam, int localTeam, int alpha) =>
        TfRobotIndicator.GlowAlpha(lastAttacked, groupTeam, localTeam, 10f).ShouldBe(alpha);

    [TestCase(TfRobotIndicator.StateDead, true, true, false, false)]
    [TestCase(TfRobotIndicator.StateActive, true, false, true, false)]
    [TestCase(TfRobotIndicator.StateShielded, true, false, false, true)]
    [TestCase(TfRobotIndicator.StateInactive, false, false, false, false)]
    public void UpdateState_ByGroupState_ShowsValvesPanel(int state, bool container, bool dead, bool active, bool shielded)
    {
        TfRobotIndicator robot = new(new VguiPanel(null, "Owner"), "red_group_1", Group(state));

        robot.UpdateState(15f);

        (robot.RobotStateContainer.Visible, robot.DeadPanel.Visible, robot.ActivePanel.Visible, robot.ShieldedPanel.Visible)
            .ShouldBe((container, dead, active, shielded));
        robot.ActivePanel.Image.Visible.ShouldBe(state != TfRobotIndicator.StateDead);
    }

    [Test]
    public void UpdateState_HalfwayThroughARespawn_FillsHalfTheDialAndShowsFive()
    {
        TfRobotIndicator robot = new(new VguiPanel(null, "Owner"), "red_group_1", Group(TfRobotIndicator.StateDead));

        robot.UpdateState(15f);

        robot.DeadPanel.RespawnProgressBar.Percentage.ShouldBe(0.5f);
        robot.DeadPanel.DialogVariable("time").ShouldBe("5");
    }

    [Test]
    public void Tick_WithItsGroupGone_HidesTheIndicator()
    {
        TfRobotIndicator robot = new(new VguiPanel(null, "Owner"), "red_group_1", Group(TfRobotIndicator.StateActive)) { Group = null };

        robot.Tick(2, 15f);

        robot.Visible.ShouldBeFalse();
    }

    /// <remarks>`GetReturnProgress` (entity_capture_flag.cpp:2583): the later of reset and neutral, over the maximum.</remarks>
    [Test]
    public void ReturnProgress_HalfwayToItsReset_IsHalf() =>
        (new SceneFlag(1, 2, 2, false, false, 5, string.Empty, default, 0, 0f) { ResetTime = 20f, NeutralTime = 10f, MaxResetTime = 10f })
            .ReturnProgress(15f).ShouldBe(0.5f);

    [TestCase(2, 7)]
    [TestCase(3, 4)]
    public void Score_ByTeam_IsThatTeams(int team, int score) =>
        new SceneRobotDestruction(false, 300, 7, 4, 0, 0, 0f, 0f, 45f, string.Empty, 0f, []).Score(team).ShouldBe(score);

    /// <remarks>:822-823: `V_snwprintf` into `wchar_t[3]` keeps two digits and the terminator.</remarks>
    [TestCase(0, "0")]
    [TestCase(42, "42")]
    [TestCase(150, "15")]
    [TestCase(-15, "-1")]
    public void LeaderScoreText_ByPoints_KeepsAtMostTwoDigits(int points, string text) =>
        TfHudRobotDestruction.LeaderScoreText(points).ShouldBe(text);

    /// <remarks>`GetVectorInHudSpace` (cdll_util.cpp:552): in front lands on the screen; behind is stomped to -640.</remarks>
    [TestCase(1f, true, 320, 240)]
    [TestCase(0.001f, true, 320, 240)]
    [TestCase(0.0009f, false, -640, -640)]
    [TestCase(-1f, false, -640, -640)]
    public void GetVectorInHudSpace_InFrontOrBehind_IsValves(float w, bool onScreen, int x, int y)
    {
        float[] matrix = new float[16];
        (matrix[0], matrix[5], matrix[10], matrix[15]) = (1f, 1f, 1f, w);

        HudSpace.GetVectorInHudSpace(matrix, (0f, 0f, 0f), 640, 480).ShouldBe((onScreen, x, y));
    }

    /// <remarks>`cvttss2si`: out of range or NaN is `INT_MIN`; .NET's cast would saturate.</remarks>
    [TestCase(12.9f, 12)]
    [TestCase(-12.9f, -12)]
    [TestCase(3e9f, int.MinValue)]
    [TestCase(-3e9f, int.MinValue)]
    [TestCase(float.NaN, int.MinValue)]
    public void Truncate_InAndOutOfRange_IsTheInstructions(float value, int truncated) =>
        HudSpace.Truncate(value).ShouldBe(truncated);

    /// <remarks>`CHandle::Get`: the index and the serial both, so a new group in the old slot is not the old one.</remarks>
    [TestCase(7, true)]
    [TestCase(8, false)]
    public void Resolve_TheSameSlotBySerial_FindsOnlyTheSameEntity(int serial, bool found)
    {
        SceneRobotDestruction logic = new(false, 300, 0, 0, 0, 0, 0f, 0f, 45f, string.Empty, 0f, [Group(TfRobotIndicator.StateActive) with { SerialNumber = serial }]);

        (TfHudRobotDestruction.Resolve(logic, Group(TfRobotIndicator.StateDead) with { SerialNumber = 7 }) is not null).ShouldBe(found);
    }

    /// <remarks>Panel.cpp:3906-3931: a reload applies the scheme at once, then `layoutNow` lays out at once.</remarks>
    [Test]
    public void InvalidateLayout_NowWithAReload_AppliesTheSchemeThenLaysOutAtOnce()
    {
        CountingPanel panel = new();

        panel.InvalidateLayout(layoutNow: true, reloadScheme: true, Context());

        panel.Calls.ShouldBe("scheme,layout");
    }

    [Test]
    public void InvalidateLayout_LaterWithAReload_AppliesTheSchemeButDoesNotLayOut()
    {
        CountingPanel panel = new();

        panel.InvalidateLayout(layoutNow: false, reloadScheme: true, Context());

        panel.Calls.ShouldBe("scheme");
    }

    private static VguiContext Context()
    {
        Content.Assets.KeyValuesTree scheme = Content.Assets.KeyValuesTree.Load(
            System.Text.Encoding.UTF8.GetBytes("Scheme { Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, 960), scheme.Find("Fonts")!, 1280, 960, "english");
    }

    private sealed class CountingPanel() : VguiPanel(null, "Counting")
    {
        public string Calls { get; private set; } = string.Empty;

        public override void ApplySchemeSettings(VguiContext context)
        {
            base.ApplySchemeSettings(context);
            Calls += Calls.Length == 0 ? "scheme" : ",scheme";
        }

        protected override void PerformLayout() => Calls += Calls.Length == 0 ? "layout" : ",layout";
    }

    private static SceneRobotGroup Group(int state) => new(125, 2, 1, state, 10f, 20f, 0f, "../HUD/hud_bot_worker_outline_red");
}
