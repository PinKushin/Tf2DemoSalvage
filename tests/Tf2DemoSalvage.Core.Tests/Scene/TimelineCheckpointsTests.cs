using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A demo walked into trimmed checkpoints must answer every question at every tick, through a window resumed from the
/// nearest checkpoint, exactly as the whole timeline does — in a scrubber's order, back and then forward (D217 step 3).
/// </summary>
public sealed class TimelineCheckpointsTests
{
    private const int Every = 2;

    private const int Lookback = 16;

    private static IEnumerable<TestCaseData> Demos()
    {
        yield return new TestCaseData(SyntheticPlayer.DemoOverTicks(0.015f, (10, 0f, 0f), (11, 4f, 0f), (12, 8f, 2f), (14, 16f, 4f), (20, 30f, 4f), (24, 31f, 9f)))
            .SetArgDisplayNames("a player moving");
        yield return new TestCaseData(SyntheticPlayer.DemoOfALateTeam(0.015f, (10, null), (12, SceneTeams.Blu), (14, SceneTeams.Red)))
            .SetArgDisplayNames("a team stated late");
        yield return new TestCaseData(SyntheticRocket.DemoOverTicks(0.015f, ownerModel: SyntheticRocket.Model, [(20, 0f, 0f, 0f), (21, 10f, 0f, 0f), (22, 20f, 5f, 0f), (24, 40f, 5f, 2f)]))
            .SetArgDisplayNames("a rocket and its owner's track");
        yield return new TestCaseData(SyntheticPlayer.DemoOfScenes(
                (10, 1, null, 4), (12, 1, 2, 5), (12, 1, 2, 6), (14, null, 2, 6), (16, null, null, 6), (18, 3, null, 6), (20, null, null, 6)))
            .SetArgDisplayNames("scenes and soundscapes");
        yield return new TestCaseData(LongWalk()).SetArgDisplayNames("a long walk");
    }

    /// <remarks>**The equivalence**: every quarter tick, latest first then earliest first, over something.</remarks>
    [TestCaseSource(nameof(Demos))]
    public void Window_AtEveryTickBackThenForward_AnswersAsTheWholeTimeline(byte[] demo)
    {
        DemoTimeline whole = DemoTimeline.Build(demo);
        DemoTimeline.Checkpoints checkpoints = DemoTimeline.Checkpoints.Walk(demo, Every, Lookback)!;
        checkpoints.Ticks.Count().ShouldBeGreaterThan(2);

        List<double> ticks = [.. Enumerable.Range(0, ((whole.LastTick - whole.FirstTick) * 4) + 1).Select(step => whole.FirstTick + (step / 4d))];
        long values = 0;

        foreach (double tick in ticks.AsEnumerable().Reverse().Concat(ticks))
        {
            DemoTimeline window = checkpoints.Window((int)tick, (int)tick + 1);
            List<(string Question, object? Answer)> expected = TimelineAnswers.At(whole, tick);
            List<(string Question, object? Answer)> actual = TimelineAnswers.At(window, tick);

            for (int index = 0; index < expected.Count; index++)
            {
                (string hash, long count) = TimelineFingerprint.OfAnswer(expected[index].Answer);
                values += count;
                TimelineFingerprint.OfAnswer(actual[index].Answer).Hash.ShouldBe(hash, $"{expected[index].Question} at {tick}");
            }
        }

        values.ShouldBeGreaterThan(ticks.Count * 20);
    }

    /// <summary>A player moving every other tick for 60 ticks: more history than a checkpoint keeps, so the trim bites.</summary>
    private static byte[] LongWalk() =>
        SyntheticPlayer.DemoOverTicks(0.015f, [.. Enumerable.Range(0, 30).Select(step => (10 + (step * 2), step * 3f, (step % 5) * 1.5f))]);

    /// <remarks>
    /// **The control is that the trim bites**: the long walk's tracks hold fewer keyframes in a late window than in the
    /// whole timeline, and every answer still agrees.
    /// </remarks>
    [Test]
    public void Window_LongWalkLate_HoldsLessAndAnswersAsTheWholeTimeline()
    {
        byte[] demo = LongWalk();
        DemoTimeline whole = DemoTimeline.Build(demo);
        DemoTimeline.Checkpoints checkpoints = DemoTimeline.Checkpoints.Walk(demo, Every, lookbackTicks: 4)!;
        int late = whole.LastTick - 3;
        DemoTimeline window = checkpoints.Window(late, late + 1);

        window.PlayerTracks.Sum(track => track.HeldKeyframeCount)
            .ShouldBeLessThan(whole.PlayerTracks.Sum(track => track.HeldKeyframeCount));

        foreach (double tick in new[] { late, late + 0.5, late + 1 })
        {
            TimelineFingerprint.OfAnswer(TimelineAnswers.At(window, tick)).Hash
                .ShouldBe(TimelineFingerprint.OfAnswer(TimelineAnswers.At(whole, tick)).Hash, $"at {tick}");
        }
    }

    /// <remarks>
    /// **Frames reach as far back as the HUD feed does** (review finding 1): `HudEventFeed.Resolve` asks for the players
    /// and rules at each replayed event's own tick, up to the output reach back. With an output reach of 20 and a track
    /// reach of 2, a window late in the long walk must answer PlayersAt 18 ticks back as the whole build does.
    /// </remarks>
    [Test]
    public void Window_PlayersAtTheOutputReachBack_AnswersAsTheWholeTimeline()
    {
        byte[] demo = LongWalk();
        DemoTimeline whole = DemoTimeline.Build(demo);
        DemoTimeline.Checkpoints checkpoints = DemoTimeline.Checkpoints.Walk(demo, Every, lookbackTicks: 20, trackLookbackTicks: 2)!;
        int late = whole.LastTick - 3;
        int back = late - 18;
        DemoTimeline window = checkpoints.Window(late, late + 1);

        whole.PlayersAt(back).ShouldNotBeEmpty("the control: someone is there to compare");
        TimelineFingerprint.OfAnswer(window.PlayersAt(back)).Hash.ShouldBe(TimelineFingerprint.OfAnswer(whole.PlayersAt(back)).Hash);
    }

    /// <remarks>
    /// **A team stated only in the demo's FINAL frame** fills every earlier frame (the first-stated roster), and a window
    /// long before it must fill it too — the final frame is observed only when the walk is finished.
    /// </remarks>
    [Test]
    public void Window_ATeamFirstStatedInTheFinalFrame_FillsAnEarlyFrame()
    {
        byte[] demo = SyntheticPlayer.DemoOfALateTeam(
            0.015f, [.. Enumerable.Range(0, 10).Select(step => (10 + (step * 2), step == 9 ? SceneTeams.Blu : (int?)null))]);
        DemoTimeline.Checkpoints checkpoints = DemoTimeline.Checkpoints.Walk(demo, Every, Lookback)!;

        DemoTimeline.Build(demo).PlayersAt(12).ShouldHaveSingleItem().Team.ShouldBe(SceneTeams.Blu, "the control: a whole build fills it");
        checkpoints.Window(12, 13).PlayersAt(12).ShouldHaveSingleItem().Team.ShouldBe(SceneTeams.Blu);
    }

    /// <remarks>
    /// **The demo's end-of-demo facts reach a window before them**: a ConVar set again and a player who joins, both at
    /// tick 30, are what a window at tick 4 reports, as the whole build does.
    /// </remarks>
    [Test]
    public void Window_FactsStatedAfterIt_ReportsTheDemosFinalOnes()
    {
        byte[] demo = SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            [
                SyntheticDemo.DataTables(new Core.Schema.DemoSchema([], [])),
                SyntheticDemo.Packet(
                    SyntheticDemo.DefaultProtocol,
                    1,
                    SyntheticDemo.StringTable("userinfo", [("0", SceneGameEventTests.Record("Alice", 7))]),
                    Gravity("800")),
                .. Enumerable.Range(2, 28).Select(tick => SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, tick)),
                SyntheticDemo.Packet(
                    SyntheticDemo.DefaultProtocol,
                    30,
                    // Entry 0 again (an update's entries are numbered from 0): Bob takes Alice's slot, and both played.
                    SyntheticDemo.UpdateTable(0, [("0", SceneGameEventTests.Record("Bob", 9))]),
                    Gravity("600")),
            ]);
        DemoTimeline.Checkpoints checkpoints = DemoTimeline.Checkpoints.Walk(demo, Every, Lookback)!;
        checkpoints.Ticks.Count().ShouldBeGreaterThan(2, "the control: the window resumes from a checkpoint, not the whole walk");

        DemoTimeline whole = DemoTimeline.Build(demo);
        whole.ServerConVars.Value("sv_gravity").ShouldBe("600", "the control: a whole build");
        whole.Roster.Keys.Order().ToArray().ShouldBe([7, 9], "the control: a whole build");

        DemoTimeline window = checkpoints.Window(4, 5);

        window.ServerConVars.Value("sv_gravity").ShouldBe("600");
        window.Roster.Keys.Order().ShouldBe([7, 9]);
    }

    private static SetConVarMessage Gravity(string value) => new([new KeyValuePair<string, string>("sv_gravity", value)]);
}
