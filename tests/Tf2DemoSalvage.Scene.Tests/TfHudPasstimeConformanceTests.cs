using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFHudPasstimeBallStatus` and its helpers (game/client/tf/tf_hud_passtime.cpp).</summary>
public sealed class TfHudPasstimeConformanceTests
{
    // pass_coastal_rc8's track, read off the demo: three points along Y at height 160.
    private static ScenePasstime Coastal(params ScenePasstimeGoal[] goals) => new(
        [(0f, -1408f, 160f), (0f, 0f, 160f), (0f, 1408f, 160f), .. new (float, float, float)[13]],
        2, float.MaxValue, 0, 268, 0, default, 0, 0, 0, goals);

    /// <remarks>
    /// `CalcProgressFrac` (:946): the running total starts at 1, so the start of the track is 1 / 2817 rather than 0 and the
    /// end is exactly 1; a tie between segments keeps the first, so the middle point reads the first segment's end.
    /// </remarks>
    [TestCase(0f, -1408f, 1f / 2817f)]
    [TestCase(0f, 1408f, 1f)]
    [TestCase(500f, 0f, 1409f / 2817f)]
    [TestCase(0f, -5000f, 1f / 2817f)]
    public void ProgressFrac_OnTheCoastalTrack_IsValves(float x, float y, float frac) =>
        Coastal().ProgressFrac((x, y, 160f)).ShouldBe(frac, 1e-6f);

    [Test]
    public void ProgressFrac_WithNoTrack_IsZero() =>
        new ScenePasstime([], 0, 0f, 0, 0, 0, default, 0, 0, 0, []).ProgressFrac((1f, 2f, 3f)).ShouldBe(0f);

    /// <remarks>`GetProgressGoalImage` (:103): "a goal that's on team blue ... should look red on the hud".</remarks>
    [TestCase(3, 0, false, "../passtime/hud/passtime_goal_red_icon")]
    [TestCase(2, 1, false, "../passtime/hud/passtime_endzone_blue_icon")]
    [TestCase(3, 2, true, "../passtime/hud/passtime_goal_red_locked")]
    [TestCase(2, 2, false, "../passtime/hud/passtime_goal_blue_unlocked")]
    public void GoalImage_ByTeamTypeAndLock_IsValves(int team, int type, bool disabled, string image) =>
        TfHudPasstimeBallStatus.GoalImage(new ScenePasstimeGoal(1, team, disabled, type, default)).ShouldBe(image);

    [TestCase(2, "../passtime/hud/passtime_ballcontrol_red")]
    [TestCase(3, "../passtime/hud/passtime_ballcontrol_blue")]
    [TestCase(0, "../passtime/hud/passtime_ballcontrol_none")]
    public void BallImage_ByTeam_IsValves(int team, string image) => TfHudPasstimeBallStatus.BallImage(team).ShouldBe(image);

    /// <remarks>`GetPlayerProgressPortrait` (:67): engineer is "eng", demoman "demo", BLU "blu"; any other class a scout.</remarks>
    [TestCase(9, 3, "../passtime/hud/portrait_eng_blu")]
    [TestCase(4, 2, "../passtime/hud/portrait_demo_red")]
    [TestCase(0, 2, "../passtime/hud/portrait_scout_red")]
    public void Portrait_ByClassAndTeam_IsValves(int playerClass, int team, string image) =>
        TfHudPasstimeBallStatus.Portrait(playerClass, team).ShouldBe(image);

    private static (TfHudPasstimeEventText Text, VguiLabel Title, VguiLabel Bonus) EventText()
    {
        TfHudPasstimeEventText text = new();
        VguiLabel title = new TfExLabel(null, "EventTitleLabel");
        VguiLabel bonus = new TfExLabel(null, "EventBonusLabel");
        text.SetControls(title, new TfExLabel(null, "EventDetailLabel"), bonus);
        return (text, title, bonus);
    }

    private static string? Strings(string tag) => tag == "#Msg_T" ? "%subject% scored" : null;

    /// <remarks>`Tick` (:588): in 0.1 s, shown 3 s, out 0.25 s, then a 0.1 s pause; `IsElapsed` is strictly past the end.</remarks>
    [Test]
    public void Tick_ANotice_RunsInShowOutPauseOnCurtime()
    {
        (TfHudPasstimeEventText text, VguiLabel title, VguiLabel bonus) = EventText();
        text.Keys["subject"] = "fiff";
        text.Enqueue(Strings, "#Msg_T", "#Missing", null);

        text.Tick(10f);
        text.State.ShouldBe(TfHudPasstimeEventText.Stage.In);
        title.Visible.ShouldBeTrue();
        bonus.Visible.ShouldBeFalse("an empty string hides its label");

        text.Tick(10.1f);
        text.State.ShouldBe(TfHudPasstimeEventText.Stage.In, "10.1 is not past 10.1");
        text.Tick(10.11f);
        text.State.ShouldBe(TfHudPasstimeEventText.Stage.Show);
        text.Tick(13.12f);
        text.State.ShouldBe(TfHudPasstimeEventText.Stage.Out);
        text.Tick(13.38f);
        text.State.ShouldBe(TfHudPasstimeEventText.Stage.Pause);
        title.Visible.ShouldBeFalse();
        text.Tick(13.49f);
        text.State.ShouldBe(TfHudPasstimeEventText.Stage.Idle);
    }

    /// <remarks>`EnterState` (:553): three or more waiting halve every stage but the pause.</remarks>
    [Test]
    public void Tick_ABackloggedQueue_HalvesTheFade()
    {
        (TfHudPasstimeEventText text, _, _) = EventText();

        for (int i = 0; i < 4; i++)
        {
            text.Enqueue(Strings, "#Msg_T", "#Msg_T", null);
        }

        text.Tick(0f);
        text.Tick(0.051f);
        text.State.ShouldBe(TfHudPasstimeEventText.Stage.Show);
    }

    [Test]
    public void Enqueue_WithoutAllThreeLabels_IsDropped()
    {
        TfHudPasstimeEventText text = new();
        text.SetControls(new TfExLabel(null, "a"), null, new TfExLabel(null, "b"));
        text.Enqueue(Strings, "#Msg_T", "#Msg_T", null);
        text.Queued.ShouldBe(0);
    }

    [Test]
    public void TeamColor_Red_IsValvesOwnNotThePlayerResources() =>
        TfHudPasstimeBallStatus.TeamColor(2, 222).ShouldBe(((byte)159, (byte)55, (byte)34, (byte)222));
}
