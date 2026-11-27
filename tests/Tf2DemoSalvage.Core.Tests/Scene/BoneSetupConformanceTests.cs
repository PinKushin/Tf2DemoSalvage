using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A server-animated prop's cycle history resets on every update whose parity differs from the parity at its last bone
/// setup (B878), which is a view-time event: what the view posed decides it.
/// </summary>
/// <remarks>
/// **`m_nPrevNewSequenceParity` is written only at a bone setup** (`c_baseanimating.cpp:716`, `:1824`, `:1835`), and
/// `PostDataUpdate` resets `m_iv_flCycle` whenever the update's parity differs from it (`:4738-4747`) — three entries at
/// the arrival holding the value just latched (`interpolatedvar.h:740-751`). Bones are set up only on demand: drawing,
/// an attachment, a hitbox, a merge (`docs/RISKS.md` B878). Fixture: updates at 10, 20, 30, 40 with cycles 0.2, 0.4, 0.6,
/// 0.8, animation times 3 behind, parity 0, 0, 1, 1 — so the update at 30 starts a sequence and resets for every view.
/// </remarks>
public sealed class BoneSetupConformanceTests
{
    private static readonly int Delay = ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

    private static readonly HashSet<int> Prop = [SyntheticAnimatingProp.EntityIndex];

    private static readonly (int Tick, int ModelIndex, float[] Pose, float? Cycle, int? Parity)[] Four =
    [
        (10, 1, [], 0.2f, 0),
        (20, 1, [], 0.4f, 0),
        (30, 1, [], 0.6f, 1),
        (40, 1, [], 0.8f, 1),
    ];

    private static DemoTimeline Timeline(params (int Tick, int ModelIndex, float[] Pose, float? Cycle, int? Parity)[] frames) =>
        DemoTimeline.Build(SyntheticAnimatingProp.Demo(
            [string.Empty, "models/props_gameplay/door.mdl"], animationLag: 3, frames));

    /// <summary>Plays from 10 a tick at a time to <paramref name="last"/>, posing where <paramref name="posed"/> says; every cycle.</summary>
    private static List<float> Cycles(
        DemoTimeline timeline, BoneSetups? setups, System.Func<int, bool> posed, int last = 40)
    {
        List<SceneProp> props = [];
        List<float> cycles = [];

        for (int tick = 10; tick <= last; tick++)
        {
            timeline.PropsAt(tick, props, setups: setups);
            cycles.Add(props.Single(prop => prop.EntityIndex == SyntheticAnimatingProp.EntityIndex).Pose.Cycle);

            if (setups is not null && posed(tick))
            {
                timeline.NoteBonesSetUp(setups, Prop, tick);
            }
        }

        return cycles;
    }

    /// <summary>
    /// **Unposed across the update at 40, the engine's history starts there**: the last setup (25) stored parity 0, the
    /// update at 40 states 1, so it resets — three entries at 40 holding 0.8. The target, 40 less the delay, precedes them,
    /// so the walk holds the oldest entry it passed: 0.8. Posed every frame, 40's parity equals the setup's (1, stored at 39),
    /// no reset, and the pair runs from the reset at 30 (0.6) to 0.8 stamped 37.
    /// </summary>
    [Test]
    public void PropsAt_UnposedAcrossAParityItsLastSetupDidNotSee_HoldsTheReset()
    {
        float unposed = Cycles(Timeline(Four), new BoneSetups(), tick => tick <= 25)[^1];
        float posed = Cycles(Timeline(Four), new BoneSetups(), _ => true)[^1];

        unposed.ShouldBe(0.8f);
        posed.ShouldBe(0.6f + (0.2f * (40 - Delay - 30) / 7f), 1e-5f, "the control: posed every frame, the reset at 30 is the floor");
    }

    /// <summary>
    /// **Posed every frame, a view sees what no view sees, at every tick** — including the window just after a sequence
    /// starts, where a further update at 32 arrives while the target is still before 30: every update is followed by a
    /// setup, so only the parity change resets, which the timeline already stores.
    /// </summary>
    [Test]
    public void PropsAt_PosedEveryFrame_IsTheAnswerWithNoViewAtEveryTick()
    {
        (int Tick, int ModelIndex, float[] Pose, float? Cycle, int? Parity)[] frames =
            [(10, 1, [], 0.2f, 0), (20, 1, [], 0.4f, 0), (30, 1, [], 0.6f, 1), (32, 1, [], 0.65f, 1), (40, 1, [], 0.8f, 1)];

        Cycles(Timeline(frames), new BoneSetups(), _ => true).ShouldBe(Cycles(Timeline(frames), null, _ => true));
    }

    /// <summary>
    /// **Never posed, every update resets** — the constructor's −1 differs from any parity (`c_baseanimating.cpp:716`) — so
    /// at 40 it holds 0.8, as unposed from 25 does.
    /// </summary>
    [Test]
    public void PropsAt_NeverPosed_HoldsTheLatestReset()
    {
        Cycles(Timeline(Four), new BoneSetups(), _ => false)[^1].ShouldBe(0.8f);
    }

    /// <summary>
    /// **The reset a view did not see is kept after it is posed again**: unposed from 26 to 44 across the update at 40,
    /// posed from 45, then 1.0 arrives at 50 stamped 47. The engine's list is the reset at 40 (0.8) and 47, so at 52 the
    /// pair runs linearly from 40. Posed every frame, the pair is 37 to 47 with the reset at 30 before it — a spline, and
    /// a different value.
    /// </summary>
    [Test]
    public void PropsAt_PosedAgainAfterAnUnseenReset_KeepsItsHistoryFromThere()
    {
        (int Tick, int ModelIndex, float[] Pose, float? Cycle, int? Parity)[] frames =
            [.. Four, (50, 1, [], 1.0f, 1)];

        float kept = Cycles(Timeline(frames), new BoneSetups(), tick => tick <= 25 || tick >= 45, last: 52)[^1];
        float posed = Cycles(Timeline(frames), new BoneSetups(), _ => true, last: 52)[^1];

        kept.ShouldBe(0.8f + (0.2f * (52 - Delay - 40) / 7f), 1e-5f);
        System.Math.Abs(kept - posed).ShouldBeGreaterThan(1e-3f, "the control: posed every frame blends from 37");
    }

    /// <summary>
    /// **A seek back keeps the setups** (*our choice*, B878): posed every frame to 40, back to 20, then to 40 with nothing
    /// posed draws what posing every frame drew — the setups recorded after 20 name no update after them until 40 is passed
    /// again, and the one at 39 saw the parity 40 states.
    /// </summary>
    [Test]
    public void PropsAt_AfterASeekBack_KeepsTheSetupsAsIfPlayedThrough()
    {
        DemoTimeline timeline = Timeline(Four);
        BoneSetups setups = new();
        List<SceneProp> props = [];

        float posed = Cycles(timeline, setups, _ => true)[^1];
        timeline.PropsAt(20, props, setups: setups);
        timeline.PropsAt(40, props, setups: setups);

        props.Single(prop => prop.EntityIndex == SyntheticAnimatingProp.EntityIndex).Pose.Cycle.ShouldBe(posed);
    }

    /// <summary>
    /// **A restated update resets too** (B878 review 2, Sonnet): `Add` collapses an identical pose into the keyframe before
    /// it, but the engine's `PostDataUpdate` runs the parity test on it. Parity 1 arrives at 110; the same pose is restated
    /// at 120 with a new animation time. A setup at 105 saw 0, so the reset is at 120.
    /// </summary>
    [Test]
    public void UnseenResetAt_ARestatedUpdateAfterAParityChange_IsTheReset()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door.mdl");
        ScenePose started = new() { NewSequenceParity = 1, Cycle = 0.5f };
        track.Add(100, new ScenePose { NewSequenceParity = 0 }, appliedAt: 100, animationAppliedAt: 97);
        track.Add(110, started, appliedAt: 110, animationAppliedAt: 107);
        track.Add(120, started, appliedAt: 120, animationAppliedAt: 117);

        track.KeyframeCount.ShouldBe(2, "the control: the restatement made no keyframe");
        track.UnseenResetAt(125, new BoneSetup(105, 0, null)).ShouldBe(120);
    }

    /// <summary>
    /// **Eight starts unseen wrap the parity back, and the engine stops resetting there** (B878 review 2, Codex): a setup at
    /// 100 saw 0; updates at 110 to 170 state 1 to 7 and each resets; 180 states 0 again, equal to the setup's, and does
    /// not. The newest reset is 170.
    /// </summary>
    [Test]
    public void UnseenResetAt_EightStartsUnseen_IsTheLastUpdateBeforeTheParityCameBack()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door.mdl");

        for (int step = 0; step <= 8; step++)
        {
            int tick = 100 + (step * 10);
            track.Add(tick, new ScenePose { NewSequenceParity = step & 7, Cycle = step / 10f }, appliedAt: tick, animationAppliedAt: tick - 3);
        }

        track.UnseenResetAt(185, new BoneSetup(100, 0, null)).ShouldBe(170);
        track.UnseenResetAt(185, new BoneSetup(180, 0, null)).ShouldBeNull("the control: a setup at 180 saw every start");
    }

    /// <summary>
    /// **Runs of updates, a restated run after the wrap, a nonzero first parity, a seen run, and a trim** (B878 review 3):
    /// parity 5 from 100 to 104 (one run of consecutive updates), 6 at 110, then eight starts at 120 to 190 (7, 0, 1, …, 5,
    /// 6), with 190's 6 restated every tick to 195. A setup at 104 saw 5, so 195's 6 differs: the reset is 195. A setup at
    /// 110 saw 6: after eight starts the parity is 6 again, so the newest reset is the end of the run before (180). A setup
    /// at 192 saw 190's 6 (review 4: a setup the parity history can produce), so nothing is new and its kept anchor stands.
    /// Trimmed at 192, inside the last run, the run before survives — and the answer stands.
    /// </summary>
    [Test]
    public void UnseenResetAt_RunsWrapsAndATrim_FindTheNewestUnseenReset()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door.mdl");

        for (int tick = 100; tick <= 104; tick++)
        {
            track.Add(tick, new ScenePose { NewSequenceParity = 5, X = tick }, appliedAt: tick, animationAppliedAt: tick - 3);
        }

        track.Add(110, new ScenePose { NewSequenceParity = 6, X = 110 }, appliedAt: 110, animationAppliedAt: 107);

        for (int step = 1; step <= 8; step++)
        {
            int tick = 110 + (step * 10);
            track.Add(tick, new ScenePose { NewSequenceParity = (6 + step) & 7, X = tick }, appliedAt: tick, animationAppliedAt: tick - 3);
        }

        for (int tick = 191; tick <= 195; tick++)
        {
            track.Add(tick, new ScenePose { NewSequenceParity = 6, X = tick }, appliedAt: tick, animationAppliedAt: tick - 3);
        }

        track.UnseenResetAt(195, new BoneSetup(104, 5, null)).ShouldBe(195, "parity 6 differs from the setup's 5");
        track.UnseenResetAt(195, new BoneSetup(110, 6, null)).ShouldBe(180, "back at 6 after eight starts: the run before");
        track.UnseenResetAt(195, new BoneSetup(192, 6, 150)).ShouldBe(150, "the control: the run before was seen; the kept anchor");

        track.TrimBefore(192);

        track.UnseenResetAt(195, new BoneSetup(110, 6, null)).ShouldBe(180, "the run before survives the trim's tail");
    }

    /// <summary>
    /// **Updates a fixed number of ticks apart are one run, and a query inside it reads the newest by then** (B878 review 4,
    /// Fable, Sonnet): parity 0 at 90, then parity 1 every two ticks from 100 to 110 — a SourceTV snapshot cadence. A setup
    /// at 95 saw 0, so asked at 105 the reset is 104, the newest update by then; a setup at 104 saw it.
    /// </summary>
    [Test]
    public void UnseenResetAt_UpdatesTwoTicksApart_IsTheNewestByTheQuery()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door.mdl");
        track.Add(90, new ScenePose { NewSequenceParity = 0 }, appliedAt: 90, animationAppliedAt: 87);

        for (int tick = 100; tick <= 110; tick += 2)
        {
            track.Add(tick, new ScenePose { NewSequenceParity = 1, X = tick }, appliedAt: tick, animationAppliedAt: tick - 3);
        }

        track.UnseenResetAt(105, new BoneSetup(95, 0, null)).ShouldBe(104);
        track.UnseenResetAt(111, new BoneSetup(95, 0, null)).ShouldBe(110, "past the run: its last");
        track.UnseenResetAt(105, new BoneSetup(104, 1, null)).ShouldBeNull("the control: a setup at 104 saw it");
    }

    /// <summary>
    /// **A run extends only by its stride** (B878 review 5, Fable): parity 0 at 90; parity 1 at 100, 102, 104, then 107.
    /// A setup at 95 saw 0. Asked at 106 the newest update is 104 — had 107 joined the two-tick run as a three-tick one,
    /// the answer would be 106, a tick with no update.
    /// </summary>
    [Test]
    public void UnseenResetAt_AGapOffTheStride_StartsANewRun()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door.mdl");
        track.Add(90, new ScenePose { NewSequenceParity = 0 }, appliedAt: 90, animationAppliedAt: 87);

        foreach (int tick in new[] { 100, 102, 104, 107 })
        {
            track.Add(tick, new ScenePose { NewSequenceParity = 1, X = tick }, appliedAt: tick, animationAppliedAt: tick - 3);
        }

        track.UnseenResetAt(106, new BoneSetup(95, 0, null)).ShouldBe(104);
        track.UnseenResetAt(108, new BoneSetup(95, 0, null)).ShouldBe(107, "the control: 107 is an update");
    }

    /// <summary>
    /// **A trim bounds the runs of a prop that never starts a sequence** (B878 review 5, Sonnet, the cavecrew reviewer):
    /// parity 0 throughout, at gaps of one and two ticks, so fifty runs. Trimmed inside the last, it keeps that run and a
    /// tail of eight — a search for the run before the parity kept all fifty, since none differs.
    /// </summary>
    [Test]
    public void TrimBefore_AParityThatNeverChanges_KeepsOnlyTheTail()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door.mdl");

        for (int run = 0; run < 50; run++)
        {
            foreach (int tick in new[] { 100 + (run * 3), 101 + (run * 3) })
            {
                track.Add(tick, new ScenePose { X = tick }, appliedAt: tick, animationAppliedAt: tick - 3);
            }
        }

        track.UpdateRunCount.ShouldBe(50, "the control: one run per pair");

        track.TrimBefore(248);

        track.UpdateRunCount.ShouldBe(9);
    }

    /// <summary>
    /// **Updates count in arrival order, the engine's** (B878 review 5, Codex): `PostDataUpdate` runs as each arrives. A
    /// setup at 105 saw 0; parity 1 arrives at 110, then an update stamped 108 arrives after it. It counts at 110, so the
    /// reset is 110 — read as its stamp it would have run the 110 run backwards and answered 108.
    /// </summary>
    [Test]
    public void UnseenResetAt_AnUpdateStampedBeforeTheNewest_CountsAtTheNewest()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door.mdl");
        track.Add(100, new ScenePose { NewSequenceParity = 0 }, appliedAt: 100, animationAppliedAt: 97);
        track.Add(110, new ScenePose { NewSequenceParity = 1, X = 1 }, appliedAt: 110, animationAppliedAt: 107);
        track.Add(108, new ScenePose { NewSequenceParity = 1, X = 2 }, appliedAt: 108, animationAppliedAt: 105);

        track.UnseenResetAt(112, new BoneSetup(105, 0, null)).ShouldBe(110);
        track.SequenceParityAt(109).ShouldBe(0, "the control: nothing of parity 1 had arrived by 109");
    }

    /// <summary>
    /// **Two updates at one tick with different parities** (B878 review 5, Codex): 0 at 100 and 110, then 1 at 110 as
    /// well. The newer states the parity a setup then stores, and a setup at 105 that saw 0 has a reset at 110; one at 110
    /// saw 1.
    /// </summary>
    [Test]
    public void UnseenResetAt_AParityChangeAtTheTickOfTheUpdateBefore_IsAtThatTick()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door.mdl");
        track.Add(100, new ScenePose { NewSequenceParity = 0 }, appliedAt: 100, animationAppliedAt: 97);
        track.Add(110, new ScenePose { NewSequenceParity = 0, X = 1 }, appliedAt: 110, animationAppliedAt: 107);
        track.Add(110, new ScenePose { NewSequenceParity = 1, X = 2 }, appliedAt: 110, animationAppliedAt: 107);

        track.SequenceParityAt(110).ShouldBe(1, "the newer of the two");
        track.UnseenResetAt(112, new BoneSetup(105, 0, null)).ShouldBe(110);
        track.UnseenResetAt(112, new BoneSetup(110, 1, null)).ShouldBeNull("the control: a setup at 110 saw it");
    }

    /// <summary>
    /// **A trim keeps the run before a wrapped parity however many runs back it is** (B878 review 4, the cavecrew reviewer):
    /// a setup at 100 saw 0; eight starts at 110 to 180 (1 to 7, then 0); 0 then restated at 180, 181, 183, 184, … 207, 208 —
    /// gaps of one and two, so ten runs. The newest reset is 170, the run before the count. Trimmed at 207, ten runs past
    /// it, a tail of eight runs would have dropped it.
    /// </summary>
    [Test]
    public void TrimBefore_ManyRunsAfterAWrap_KeepsTheRunBeforeTheCount()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door.mdl");
        track.Add(100, new ScenePose { NewSequenceParity = 0 }, appliedAt: 100, animationAppliedAt: 97);

        for (int step = 1; step <= 7; step++)
        {
            int tick = 100 + (step * 10);
            track.Add(tick, new ScenePose { NewSequenceParity = step, X = tick }, appliedAt: tick, animationAppliedAt: tick - 3);
        }

        for (int run = 0; run < 10; run++)
        {
            foreach (int tick in new[] { 180 + (run * 3), 181 + (run * 3) })
            {
                track.Add(tick, new ScenePose { NewSequenceParity = 0, X = tick }, appliedAt: tick, animationAppliedAt: tick - 3);
            }
        }

        BoneSetup seen = new(100, 0, null);
        track.UnseenResetAt(208, seen).ShouldBe(170, "the control: before the trim");

        track.TrimBefore(207);

        track.UnseenResetAt(208, seen).ShouldBe(170);
    }

    /// <summary>
    /// **The reset seeds the update's networked cycle, not the last one latched** (B878 review 2, Sonnet, the cavecrew
    /// reviewer): the update at 40 starts nothing new (parity 1, as at 30) and keeps 30's animation time, so its 0.8 latches
    /// no entry — but `Reset()` seeds `m_pValue`, 0.8 (`interpolatedvar.h:745`). Never posed, at 42 the target precedes the
    /// reset at 40, which holds 0.8; seeding the last entry would hold 30's 0.6.
    /// </summary>
    [Test]
    public void PropsAt_AResetWhoseUpdateLatchedNoCycle_SeedsTheUpdatesCycle()
    {
        DemoTimeline timeline = DemoTimeline.ForTracks([Unlatched()]);
        List<SceneProp> props = [];

        timeline.PropsAt(42, props, setups: new BoneSetups());

        props.Single().Pose.Cycle.ShouldBe(0.8f);
    }

    /// <summary>
    /// **A never-posed prop stays on the lerp list only while its anchored answer can move** (B878 review 1, Fable; review 2,
    /// the cavecrew reviewer): at 42 the target (42 less the delay) precedes the reset at 40, where nothing brackets and the
    /// engine reports no settle — lerping, where the plain history (newest 27) is settled. Once the target passes 40 the reset
    /// holds and settles.
    /// </summary>
    [Test]
    public void PropsAt_ANeverPosedProp_LerpsUntilItsResetIsPassed()
    {
        DemoTimeline viewed = DemoTimeline.ForTracks([Unlatched()]);
        DemoTimeline plain = DemoTimeline.ForTracks([Unlatched()]);
        BoneSetups setups = new();
        List<SceneProp> props = [];

        viewed.PropsAt(42, props, setups: setups);
        plain.PropsAt(42, props);

        (viewed.TracksLerping, plain.TracksLerping).ShouldBe((1, 0));

        viewed.PropsAt(41 + Delay, props, setups: setups);
        viewed.TracksLerping.ShouldBe(0, "past the reset it holds, settled");
    }

    /// <summary>Updates at 10, 20, 30, 40, cycles 0.2 to 0.8, parity 0, 0, 1, 1; 40 keeps 30's animation time (27).</summary>
    private static ScenePropTrack Unlatched()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door.mdl");
        track.Add(10, new ScenePose { Cycle = 0.2f, NewSequenceParity = 0 }, appliedAt: 10, animationAppliedAt: 7);
        track.Add(20, new ScenePose { Cycle = 0.4f, NewSequenceParity = 0 }, appliedAt: 20, animationAppliedAt: 17);
        track.Add(30, new ScenePose { Cycle = 0.6f, NewSequenceParity = 1 }, appliedAt: 30, animationAppliedAt: 27);
        track.Add(40, new ScenePose { Cycle = 0.8f, NewSequenceParity = 1 }, appliedAt: 40, animationAppliedAt: 27);
        return track;
    }

    /// <summary>
    /// **The reset is at the newest update, whether or not it latched the cycle** (B878 review 1, Fable): the parity test
    /// runs in every update's `PostDataUpdate`. A setup at 105 saw parity 0; 1 arrives at 110 with a new animation time, then
    /// an update at 120 moves only the origin. Asked at 125 the reset is at 120, the newest update — which has no cycle entry.
    /// </summary>
    [Test]
    public void UnseenResetAt_AnOriginOnlyUpdateAfterAParityChange_IsTheReset()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door.mdl");
        track.Add(100, new ScenePose { NewSequenceParity = 0 }, appliedAt: 100, animationAppliedAt: 97);
        track.Add(110, new ScenePose { NewSequenceParity = 1, Cycle = 0.5f }, appliedAt: 110, animationAppliedAt: 107);
        track.Add(120, new ScenePose { NewSequenceParity = 1, Cycle = 0.5f, X = 8f }, appliedAt: 120, animationAppliedAt: 107);

        track.UnseenResetAt(125, new BoneSetup(105, 0, null)).ShouldBe(120);
        track.UnseenResetAt(125, new BoneSetup(120, 1, null)).ShouldBeNull("the control: a setup at 120 saw parity 1");
    }
}
