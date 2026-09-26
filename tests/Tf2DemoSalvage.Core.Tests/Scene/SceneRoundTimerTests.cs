using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`C_TeamRoundTimer` (teamplay_round_timer.cpp) and the rules the time panel reads, per frame.</summary>
public sealed class SceneRoundTimerTests
{
    [Test]
    public void RoundTimersAt_ATimer_ReadsEveryField()
    {
        SceneRoundTimer timer = DemoTimeline.Build(SyntheticPlayer.DemoWithRoundTimer(paused: true)).RoundTimersAt(100).ShouldHaveSingleItem();

        timer.ShouldBe(new SceneRoundTimer(42)
        {
            Paused = true,
            TimeRemaining = 95.5f,
            EndTime = 300.25f,
            MaxLength = 600,
            Disabled = false,
            ShowInHud = true,
            Length = 240,
            SetupLength = 60,
            State = SceneRoundTimer.StateNormal,
            ShowTimeRemaining = true,
            CaptureWatch = false,
            StopWatch = false,
            TotalTime = 12.5f,
        });
    }

    [Test]
    public void RulesAt_TheTimePanelsRules_AreRead()
    {
        SceneGameRules rules = DemoTimeline.Build(SyntheticPlayer.DemoWithRoundTimer(paused: false)).RulesAt(100);

        (rules.WaitingForPlayers, rules.Overtime, rules.Setup, rules.StopWatch, rules.GameType, rules.Koth, rules.ShowMatchSummary, rules.TimerToShowInHud)
            .ShouldBe((true, true, true, true, 4, true, true, 42));
        (rules.BlueKothTimer, rules.RedKothTimer).ShouldBe((42, (int?)null));
    }

    [TestCase(false, 300.25f, 100f, 200.25f)]
    [TestCase(false, 300.25f, 400f, 0f)]
    [TestCase(true, 300.25f, 100f, 95.5f)]
    public void TimeRemaining_PausedOrRunning_IsTheEngines(bool paused, float endTime, float curTime, float expected) =>
        new SceneRoundTimer(1) { Paused = paused, EndTime = endTime, TimeRemaining = 95.5f }.TimeRemainingAt(curTime).ShouldBe(expected);

    [Test]
    public void TimeRemaining_AStopwatchInCaptureWatch_IsTheTotalTime() =>
        new SceneRoundTimer(1) { StopWatch = true, CaptureWatch = true, TotalTime = 12.5f, EndTime = 300f }.TimeRemainingAt(100f).ShouldBe(12.5f);

    [TestCase(SceneRoundTimer.StateSetup, 600, 240, 60)]
    [TestCase(SceneRoundTimer.StateNormal, 600, 240, 600)]
    [TestCase(SceneRoundTimer.StateNormal, 0, 240, 240)]
    public void MaxLength_ByState_IsTheEngines(int state, int maxLength, int length, int expected) =>
        new SceneRoundTimer(1) { State = state, MaxLength = maxLength, Length = length, SetupLength = 60 }.TimerMaxLength.ShouldBe(expected);
}
