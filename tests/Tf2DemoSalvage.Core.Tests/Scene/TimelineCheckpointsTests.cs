using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
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

    /// <remarks>
    /// **The direct control** (D217 step 4): two windows built from each checkpoint leave every value it holds — its
    /// shared frozen property bags included — as it was. Answers alone can agree while a checkpoint is being written.
    /// </remarks>
    [Test]
    public void WindowsWriteTheirCheckpoint_EveryCheckpointOfALongWalk_IsFalse()
    {
        DemoTimeline.Checkpoints checkpoints = DemoTimeline.Checkpoints.Walk(LongWalk(), Every, Lookback)!;
        int count = checkpoints.Ticks.Count();
        count.ShouldBeGreaterThan(2);

        Enumerable.Range(0, count).Where(checkpoints.WindowsWriteTheirCheckpoint).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The owner census has its control** (D217 step 4): owners counted alone can only double-count what they share,
    /// so every type's elements summed over owners reach the whole resume's; a shortfall is an owner the census skipped.
    /// </remarks>
    [Test]
    public void ResumeCensusByOwner_EveryType_ReachesTheWholeResume()
    {
        DemoTimeline.Checkpoints checkpoints = DemoTimeline.Checkpoints.Walk(LongWalk(), Every, Lookback)!;
        int middle = checkpoints.Ticks.Count() / 2;
        Dictionary<Type, (long Objects, long Elements)> whole = checkpoints.ResumeCensus(middle);
        Dictionary<Type, long> byOwners = checkpoints.ResumeCensusByOwner(middle)
            .GroupBy(row => row.Type).ToDictionary(group => group.Key, group => group.Sum(row => row.Elements));

        whole.Count.ShouldBeGreaterThan(5);
        whole.Where(entry => byOwners.GetValueOrDefault(entry.Key) < entry.Value.Elements).Select(entry => entry.Key.Name).ShouldBeEmpty();
    }

    /// <remarks>
    /// **Eight windows of one span, released together**: built at the same moment from one checkpoint (the prefetch's
    /// case, where frozen bags' read caches are shared), every one answers as the whole timeline does and the checkpoint
    /// is left unchanged.
    /// </remarks>
    [Test]
    public void Window_EightReleasedTogetherFromOneCheckpoint_AllAnswerAsTheWholeAndLeaveItUnchanged()
    {
        byte[] demo = LongWalk();
        DemoTimeline whole = DemoTimeline.Build(demo);
        TimelineWindows walk = TimelineWindows.Walk(demo, Every, new WindowSettings()).ShouldNotBeNull();
        int start = walk.Ticks[2];
        int through = walk.Ticks[3] - 1;
        DemoTimeline[] windows = new DemoTimeline[8];
        using System.Threading.Barrier together = new(windows.Length);

        System.Threading.Tasks.Task.WaitAll(
        [
            .. Enumerable.Range(0, windows.Length).Select(index => System.Threading.Tasks.Task.Factory.StartNew(
                () =>
                {
                    together.SignalAndWait();
                    windows[index] = walk.Window(start, through);
                },
                System.Threading.CancellationToken.None,
                System.Threading.Tasks.TaskCreationOptions.LongRunning,
                System.Threading.Tasks.TaskScheduler.Default)),
        ]);

        windows.Select(window => WindowEquivalence.Compare(whole, window, through - 0.5).Differing.Count).ShouldAllBe(count => count == 0);
        walk.WindowsLeaveCheckpointUnchanged(2).ShouldBeTrue();
    }

    /// <remarks>
    /// **The comparison made a question at a time is the one made at once, and it can fail**: a window asked about a tick
    /// far outside its span differs from the whole timeline, stepped one question per call through every question and
    /// then done, the same questions both ways; the window asked inside its span does not differ.
    /// </remarks>
    [Test]
    public void Step_AWindowAskedOutsideItsSpan_DiffersAsTheOneShotCompareDoes()
    {
        byte[] demo = LongWalk();
        DemoTimeline whole = DemoTimeline.Build(demo);
        TimelineWindows walk = TimelineWindows.Walk(demo, Every, new WindowSettings()).ShouldNotBeNull();
        DemoTimeline early = walk.Window(walk.Ticks[1], walk.Ticks[2] - 1);
        double late = whole.LastTick - 0.5;

        WindowEquivalence stepped = WindowEquivalence.Start(whole, early, late);
        int steps = 0;

        while (!stepped.Step())
        {
            steps++;
        }

        stepped.Differing.ShouldNotBeEmpty();
        stepped.Differing.ShouldBe(WindowEquivalence.Compare(whole, early, late).Differing);
        (steps, stepped.Done).ShouldBe((25, true));
        WindowEquivalence.Compare(whole, early, walk.Ticks[1] + 0.5).Differing.ShouldBeEmpty();
    }

    /// <remarks>
    /// **A window cancelled mid-decode stops at the next command, throws, and is never handed out**: the token set as the
    /// third command is reached, no fourth is decoded.
    /// </remarks>
    [Test]
    public void Window_CancelledAtTheThirdCommand_DecodesNoFourthAndThrows()
    {
        TimelineWindows walk = TimelineWindows.Walk(LongWalk(), Every, new WindowSettings()).ShouldNotBeNull();
        using System.Threading.CancellationTokenSource cancel = new();
        int reached = 0;

        Should.Throw<OperationCanceledException>(() => walk.Window(
            walk.Ticks[1],
            int.MaxValue - 1,
            () =>
            {
                if (++reached == 3)
                {
                    cancel.Cancel();
                }
            },
            cancel.Token));

        reached.ShouldBe(3);
    }

    /// <remarks>**A walk cancelled before it starts stops at its first packet and throws** — what Dispose relies on.</remarks>
    [Test]
    public void Walk_Cancelled_StopsAndThrows()
    {
        using System.Threading.CancellationTokenSource cancel = new();
        cancel.Cancel();

        Should.Throw<OperationCanceledException>(() => TimelineWindows.Walk(LongWalk(), Every, new WindowSettings(), cancel.Token));
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
    /// **Frames are kept at the event ticks the HUD feed reaches back to, and only there** (review findings 1, 1b):
    /// `HudEventFeed.Resolve` asks for the players and rules at each replayed event's own tick, so the frame in force at
    /// a death 30-odd ticks back must survive a track reach of 2 — and the frames between it and the track cut need not.
    /// </remarks>
    [Test]
    public void Window_AnEventInsideTheFeedSpan_KeepsItsFrameAndNotTheOthers()
    {
        byte[] demo = LongWalkWithADeathAt(30);
        DemoTimeline whole = DemoTimeline.Build(demo);
        DemoTimeline.Checkpoints checkpoints = DemoTimeline.Checkpoints.Walk(demo, Every, lookbackTicks: 40, trackLookbackTicks: 2)!;
        int late = whole.LastTick - 3;
        DemoTimeline window = checkpoints.Window(late, late + 1);

        window.GameEvents.ShouldContain(fired => fired.Tick == 30, "the control: the death is inside the feed span");
        whole.PlayersAt(30).ShouldNotBeEmpty("the control: someone is there to compare");
        TimelineFingerprint.OfAnswer(window.PlayersAt(30)).Hash.ShouldBe(TimelineFingerprint.OfAnswer(whole.PlayersAt(30)).Hash);
        window.Frames.Count(frame => frame.Tick < late - 4).ShouldBeLessThanOrEqualTo(2, "only the event's frame and the one in force at the track cut");
    }

    /// <remarks>
    /// **A player is sampled from the occupant of his slot AT the tick** (review item 4): slot 1 is one player from 10 to
    /// 30 and another from 40 to 60. At 20.5 the drawn position is the first occupant's interpolated one — not the frame's
    /// raw position, which is what a lookup of the slot's LAST occupant (not alive at 20.5) falls back to. A window,
    /// which has not yet decoded the second occupant, would otherwise disagree with the whole build here.
    /// </remarks>
    [Test]
    public void PlayersAt_InAReusedSlotBeforeItsLastOccupant_SamplesTheOccupantThen()
    {
        DemoTimeline whole = DemoTimeline.Build(SyntheticPlayer.DemoOfAReusedSlot(
            0.015f,
            [
                .. Enumerable.Range(10, 21).Select(tick => (tick, tick * 4f, 1)),
                .. Enumerable.Range(40, 21).Select(tick => (tick, -tick * 4f, 2)),
            ]));
        ScenePose expected = whole.TrackFor(1, 20.5).ShouldNotBeNull("the control: an occupant is alive at 20.5").At(20.5).ShouldNotBeNull();
        whole.PlayerTracks.Count(track => track.EntityIndex == 1).ShouldBe(2, "the control: the slot was reused");

        List<ScenePlayer> players = [];
        whole.PlayersAt(20.5, players);

        players.ShouldHaveSingleItem().X.ShouldBe(expected.X);
        expected.X.ShouldNotBe(whole.PlayersAt(20).Single().X, "the control: interpolated is not the frame's own");
    }

    /// <remarks>
    /// **A scene run that stopped before the cut is trimmed** (review item 2): it covers no tick a window answers. Late in
    /// the scenes demo, with a reach of 2, the window holds fewer runs than the whole build and still answers alike.
    /// </remarks>
    [Test]
    public void Window_LateInTheScenesDemo_HoldsOnlyTheRunsStillLive()
    {
        byte[] demo = SyntheticPlayer.DemoOfScenes(
            (10, 1, null, 4), (12, 1, 2, 5), (12, 1, 2, 6), (14, null, 2, 6), (16, null, null, 6), (18, 3, null, 6), (20, null, null, 6),
            (22, null, null, 6), (24, null, null, 6));
        DemoTimeline whole = DemoTimeline.Build(demo);
        DemoTimeline window = DemoTimeline.Checkpoints.Walk(demo, Every, lookbackTicks: 2)!.Window(23, 24);

        whole.Scenes.Count(run => run.StoppedTick < 20).ShouldBeGreaterThan(0, "the control: runs stopped well before the window");
        window.Scenes.Count.ShouldBeLessThan(whole.Scenes.Count);
        TimelineFingerprint.OfAnswer(TimelineAnswers.At(window, 23)).Hash.ShouldBe(TimelineFingerprint.OfAnswer(TimelineAnswers.At(whole, 23)).Hash);
    }

    /// <remarks>
    /// **The comparison asks about the events the GIVEN settings replay**: at tick 60 a death at 30 is inside the default
    /// 12 s span and outside a 0.1 s one (0.2 s doubled, 14 ticks).
    /// </remarks>
    [Test]
    public void At_EventsWorldUnderShorterFeedSettings_LeavesOutTheOlderDeath()
    {
        DemoTimeline whole = DemoTimeline.Build(LongWalkWithADeathAt(30));

        static object? EventsWorld(List<(string Question, object? Answer)> answers) =>
            answers.Single(answer => answer.Question == "events' world").Answer;

        ((System.Collections.ICollection)EventsWorld(TimelineAnswers.At(whole, 60))!).Count.ShouldBe(1, "the control");
        ((System.Collections.ICollection)EventsWorld(TimelineAnswers.At(whole, 60, new WindowSettings(DeathNoticeSeconds: 0.1d, SayTextSeconds: 0.1d)))!)
            .Count.ShouldBe(0);
    }

    /// <summary>The long walk with a `player_death` game event in its packet at <paramref name="tick"/>.</summary>
    private static byte[] LongWalkWithADeathAt(int tick)
    {
        GameEventListMessage declaration = new(
            [new GameEventDefinition(1, "player_death", [new GameEventField("userid", GameEventValueType.Short)])]);
        GameEventMessage death = new(1, "player_death", new Dictionary<string, object?> { ["userid"] = (short)2 });
        List<DemoCommand> commands = [];

        foreach (DemoCommand command in DemoCommandReader.Read(LongWalk().AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type == DemoCommandType.Stop)
            {
                continue;
            }

            commands.Add(command);

            if (command.Type == DemoCommandType.Packet && command.Tick == tick)
            {
                commands.Add(SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, tick, declaration, death));
            }
        }

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
    }

    /// <remarks>
    /// **A window finds a slot's tracks through the slot index** (B548: `TrackFor` and the move-child walk read
    /// `_tracksBySlot`, built in the constructor). With a child hanging from a moving parent, a window late in the demo
    /// finds the parent's and the child's track at the tick, as the whole build does, and samples the parent the same.
    /// </remarks>
    [Test]
    public void Window_AChildHangingFromAMovingParent_FindsBothTracksAsTheWholeTimelineDoes()
    {
        byte[] demo = ParentAndChild([.. Enumerable.Range(0, 20).Select(step => (10 + (step * 2), 64f + (step * 10f)))]);
        DemoTimeline whole = DemoTimeline.Build(demo);
        DemoTimeline.Checkpoints checkpoints = DemoTimeline.Checkpoints.Walk(demo, Every, lookbackTicks: 4)!;
        int late = whole.LastTick - 3;
        DemoTimeline window = checkpoints.Window(late, late + 1);

        foreach (int slot in new[] { ParentSlot, ChildSlot })
        {
            ScenePropTrack expected = whole.TrackFor(slot, late).ShouldNotBeNull($"the control: the whole build has slot {slot}");
            ScenePropTrack actual = window.TrackFor(slot, late).ShouldNotBeNull($"slot {slot}");
            (actual.SerialNumber, actual.FirstTick).ShouldBe((expected.SerialNumber, expected.FirstTick));
        }

        TimelineFingerprint.OfAnswer(window.TrackFor(ParentSlot, late)!.At(late)).Hash
            .ShouldBe(TimelineFingerprint.OfAnswer(whole.TrackFor(ParentSlot, late)!.At(late)).Hash);
    }

    private const int ParentSlot = 7;

    private const int ChildSlot = 8;

    private const float ChildOffset = 2f;

    /// <summary>A parent prop moving along X at each (tick, x), and a child parented to it 2 units along X.</summary>
    private static byte[] ParentAndChild(params (int Tick, float X)[] frames)
    {
        DemoSchema schema = new(
            [
                new SendTable("DT_BaseEntity", NeedsDecoder: true,
                [
                    new SendProperty(SendPropType.Int, "m_nModelIndex", 1, string.Empty, 0f, 0f, 13, 0),
                    new SendProperty(SendPropType.VectorXY, "m_vecOrigin", 1, string.Empty, -16384f, 16384f, 32, 0),
                    new SendProperty(SendPropType.Float, "m_vecOrigin[2]", 1, string.Empty, -16384f, 16384f, 32, 0),
                    new SendProperty(SendPropType.Int, "moveparent", 1, string.Empty, 0f, 0f, 21, 0),
                ]),
                new SendTable("DT_DynamicProp", NeedsDecoder: true,
                    [new SendProperty(SendPropType.DataTable, "baseentity", 1, "DT_BaseEntity", 0f, 0f, 0, 0)]),
            ],
            [new ServerClass(0, "CDynamicProp", "DT_DynamicProp")]);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(1));
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(0);
        const int Serial = 3;
        const int NoParent = (1 << 21) - 1;

        List<DemoCommand> commands =
        [
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0,
                SyntheticDemo.StringTable(ModelPrecache.TableName, [string.Empty, "models/a.mdl"], maxEntries: 8)),
            SyntheticDemo.DataTables(schema),
        ];

        for (int index = 0; index < frames.Length; index++)
        {
            DecodedEntity Prop(int slot, float x, int parent) => new(
                slot,
                0,
                SerialNumber: Serial,
                index == 0 ? EntityUpdateType.Enter : EntityUpdateType.Delta,
                [
                    .. new (string Name, PropertyValue Value)[]
                    {
                        ("m_nModelIndex", PropertyValue.FromInt(1)),
                        ("m_vecOrigin", PropertyValue.FromVectorXY(x, 0f)),
                        ("m_vecOrigin[2]", PropertyValue.FromFloat(0f)),
                        ("moveparent", PropertyValue.FromInt(parent)),
                    }
                    .Select(property =>
                    {
                        int at = flat.ToList().FindIndex(candidate => candidate.Property.Name == property.Name);
                        return new DecodedProperty(at, flat[at], property.Value);
                    })
                    .OrderBy(property => property.Index),
                ]);

            byte[] body = decoder.EncodeEntities(
                [Prop(ParentSlot, frames[index].X, NoParent), Prop(ChildSlot, ChildOffset, ParentSlot | (Serial << 11))],
                [], isDelta: index > 0, 0, out int bits);

            commands.Add(SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, frames[index].Tick, new PacketEntitiesMessage(
                MaxEntries: 64,
                IsDelta: index > 0,
                DeltaFromTick: index > 0 ? frames[index - 1].Tick : null,
                BaselineIndex: false,
                UpdatedEntries: 2,
                LengthBits: bits,
                UpdateBaseline: false,
                Body: body)));
        }

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
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
                SyntheticDemo.DataTables(new DemoSchema([], [])),
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
