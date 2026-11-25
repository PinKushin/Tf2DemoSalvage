using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What the engine draws after `GetClientInterpAmount()` RISES mid-recording (B860).
/// </summary>
/// <remarks>
/// `CheckCLInterpChanged` (`c_baseentity.cpp:6443-6465`) sets each variable's amount and nothing else: an entity
/// whose variables had settled (`m_bNeedsToInterpolate` false, `c_baseentity.cpp:887`) is not interpolated again
/// until an update latches (`OnLatchInterpolatedVariables`, `:2806-2836`), so it holds. A variable still
/// interpolating had its history pruned by the amount it ran with (`RemoveEntriesPreviousTo( currentTime -
/// interpolation_amount - EXTRA_INTERPOLATION_HISTORY_STORED )`, `interpolatedvar.h:1057`, keeping the first
/// entry older than the cut and two more, `:791`), and a target older than every entry it kept answers the
/// oldest (`:877-882`).
/// </remarks>
public sealed class InterpolationRiseConformanceTests
{
    private const double Interval = 0.015d;

    /// <summary>A delay of 8 ticks (TF2's defaults) until tick 50, then 34 (both ratio bounds 10: 0.5 s).</summary>
    internal static InterpolationSchedule RiseAtFifty()
    {
        ServerConVars raised = new();
        raised.Apply(new SetConVarMessage(
        [
            new KeyValuePair<string, string>("sv_client_min_interp_ratio", "10"),
            new KeyValuePair<string, string>("sv_client_max_interp_ratio", "10"),
        ]));
        return InterpolationSchedule.From(new ClientInterp(), new ServerConVars(), [(50, raised)], Interval);
    }

    /// <summary>
    /// **A settled entity holds across a rise.** A crate slides 0 to 300 over ticks 0 to 30 and rests; by tick 38 its
    /// target (38 − 8 = 30) has reached the last entry, every variable has settled and the entity left the list. At 50
    /// the delay becomes 34, so a fresh sample would aim at 16, mid-slide at 160; the engine never interpolates it
    /// again, so it is drawn at 300 until an update arrives.
    /// </summary>
    [Test]
    public void At_ASettledEntityAfterTheDelayRises_HoldsWhereItRested()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        for (int tick = 0; tick <= 30; tick += 3)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });
        }

        (crate.At(49)!.Value.X, crate.At(60)!.Value.X).ShouldBe((300f, 300f));
    }

    /// <summary>
    /// **An entity that settles on the frame that reads the rise still holds** (B860): its last update is at 42. The frame
    /// at 50 starts under the old delay, aims at 42, reaches the last entry and leaves the list; only the frame at 51
    /// learns the new amount, and nothing interpolates the crate any more. So 420 at 60, where a sample taken under the new
    /// delay would aim at 26 and draw the oldest entry the history kept, 300.
    /// </summary>
    [Test]
    public void At_AnEntityThatSettlesOnTheFrameThatReadsTheRise_HoldsWhereItRested()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        for (int tick = 0; tick <= 42; tick += 3)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });
        }

        crate.At(60)!.Value.X.ShouldBe(420f);
    }

    /// <summary>
    /// **A variable that had settled was last pruned on its settle frame** (B860 review 1, Fable): `Interpolate` prunes
    /// only a listed variable (`interpolatedvar.h:1057`, skipped once settled, `c_baseentity.cpp:879-887`), and
    /// `NoteChanged` prunes at a latch (`interpolatedvar.h:667`). Updates every tick to 40, then the next at 55. The frame at
    /// 48 aims at 40, settles, and cuts at 40 − 3.33 = 36.67, keeping 36, 35 and 34; the latch at 55 cuts at 55 − 34 − 3.33
    /// and drops nothing. Aiming at 21, the frame at 55 draws the oldest it holds, 34: 340. Pruning on every frame to the
    /// rise would have cut at 42 − 3.33 and drawn 36, 360.
    /// </summary>
    [Test]
    public void At_AnEntitySettledBeforeTheRiseThenUpdated_ReachesTheHistoryItsSettleFrameKept()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        for (int tick = 0; tick <= 40; tick++)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });
        }

        crate.Add(55, new ScenePose { X = 550f });

        crate.At(55)!.Value.X.ShouldBe(340f);
    }

    /// <summary>
    /// **A moving entity cannot reach history the old delay pruned.** The crate slides on, an update every three ticks
    /// at ten units a tick. At 49 the target was 41 and the frame pruned before 41 − 3.33 = 37.67 (0.05 s at 0.015 s a
    /// tick): the first entry older than that is 36, kept with the two before it, 33 and 30. At 50 the new delay aims
    /// at 16; the oldest entry the engine still holds is 30, so it draws that entry, 300, not 160.
    /// </summary>
    [Test]
    public void At_AMovingEntityAfterTheDelayRises_ReachesNoFurtherBackThanTheOldDelayKept()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        for (int tick = 0; tick <= 99; tick += 3)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });
        }

        crate.At(50)!.Value.X.ShouldBe(300f);
    }

    /// <summary>
    /// **The frame that reads the rise still draws under the old delay** (B860): `CEngine::Frame` sends `FRAME_START`, where
    /// `CheckCLInterpChanged` hands each variable the new amount, before the host frame's `CL_ReadPackets` reads tick 50.
    /// Frames a tick apart: the frame at 50 started having read 49, so it aims at 50 − 8 = 42 and draws 420; the frame at
    /// 51 started at 50, aims at 51 − 34 = 17, and the history the frame at 50 kept (cut at 42 − 3.33, so 36, 33 and 30)
    /// floors it at 300.
    /// </summary>
    [Test]
    public void PropsAt_TheFrameThatReadsARise_DrawsUnderTheDelayItStartedWith()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        for (int tick = 0; tick <= 99; tick += 3)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });
        }

        DemoTimeline timeline = DemoTimeline.ForTracks([crate]);
        List<SceneProp> props = [];

        timeline.FrameStart(49, 50);
        timeline.PropsAt(50, props);
        float atTheRise = props.ShouldHaveSingleItem().Pose.X;

        timeline.FrameStart(50, 51);
        timeline.PropsAt(51, props);

        new[] { atTheRise, props.ShouldHaveSingleItem().Pose.X }.ShouldBe([420f, 300f], 0.01f);
    }

    /// <summary>
    /// The control: a frame that started at the rise's own tick draws under the new delay at once, the floor of
    /// <see cref="At_AMovingEntityAfterTheDelayRises_ReachesNoFurtherBackThanTheOldDelayKept"/>.
    /// </summary>
    [Test]
    public void PropsAt_AFrameThatStartedAtTheRise_DrawsUnderTheNewDelay()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        for (int tick = 0; tick <= 99; tick += 3)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });
        }

        DemoTimeline timeline = DemoTimeline.ForTracks([crate]);
        List<SceneProp> props = [];

        timeline.FrameStart(50, 50);
        timeline.PropsAt(50, props);

        props.ShouldHaveSingleItem().Pose.X.ShouldBe(300f, 0.01f);
    }

    /// <summary>
    /// **A jump forward is read a hundred packets at a time** (B860 review 1): while skipping, `CDemoPlayer::ReadPacket`
    /// stops after its hundredth call (`engine.dll` 0x1800741e0) and every frame of the skip starts afresh. A packet a tick,
    /// a jump from 10 to 180 across the rise at 50: the frame at 180 had read at least to 80, so it knows the rise and
    /// aims at 180 − 34 = 146, 1460; started at 10 it would aim at 180 − 8 = 172, 1720.
    /// </summary>
    [Test]
    public void PropsAt_AFrameAfterAJumpForward_KnowsWhatTheLastSkipBatchRead()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };
        List<TimelineFrame> frames = [];

        for (int tick = 0; tick <= 199; tick++)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });
            frames.Add(new TimelineFrame(tick, []));
        }

        DemoTimeline timeline = DemoTimeline.ForTracks([crate], frames);
        List<SceneProp> props = [];

        timeline.FrameStart(10, 180);
        timeline.PropsAt(180, props);

        props.ShouldHaveSingleItem().Pose.X.ShouldBe(1460f, 0.01f);
    }

    /// <summary>
    /// **A frame's start is that frame's, not the timeline's** (B860 review 1, Sonnet): the presenter's frame at 50 started
    /// having read 49, and another reader then asks for 51 — prediction looking ahead, a seek's scan. That is no frame of
    /// the presenter's and samples under its own tick, 51 − 34 = 17 floored at 30, so 300; carrying the frame start over
    /// would aim at 51 − 8 = 43, 430.
    /// </summary>
    [Test]
    public void PropsAt_AnotherReadersTickAfterAFrameStart_IsSampledUnderItsOwnTick()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        for (int tick = 0; tick <= 99; tick += 3)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });
        }

        DemoTimeline timeline = DemoTimeline.ForTracks([crate]);
        List<SceneProp> props = [];

        timeline.FrameStart(49, 50);
        timeline.PropsAt(51, props);

        props.ShouldHaveSingleItem().Pose.X.ShouldBe(300f, 0.01f);
    }

    /// <summary>A rocket launcher, entity 2, updated every tick at ten units a tick, owned by <paramref name="owner"/>.</summary>
    internal static ScenePropTrack Launcher(int? owner)
    {
        ScenePropTrack weapon = new(entityIndex: 2, "models/weapons/w_models/w_rocketlauncher.mdl")
        {
            Interpolation = RiseAtFifty(),
            PredictsForItsOwner = true,
            OwnedBy = owner,
        };

        for (int tick = 0; tick <= 99; tick++)
        {
            weapon.Stamp(tick);
            weapon.Add(tick, new ScenePose { X = tick * 10f });
        }

        return weapon;
    }

    /// <summary>
    /// The control: the same launcher owned by somebody else is not predicted. It aims at 60 − 34 = 26, floored at the
    /// oldest entry the frame at 50 kept: that frame aimed at 42 and cut at 42 − 3.33 = 38.67, keeping 38 and the two
    /// before it, so 36, 360.
    /// </summary>
    [Test]
    public void PropsAt_SomebodyElsesWeapon_TakesTheSchedulesDelay()
    {
        DemoTimeline timeline = DemoTimeline.ForTracks([Launcher(owner: 3)], recorder: 1);
        List<SceneProp> props = [];

        timeline.PropsAt(60, props);

        props.ShouldHaveSingleItem().Pose.X.ShouldBe(360f, 0.01f);
    }

    /// <summary>
    /// **A server that forces `cl_predict` off predicts nothing** (B860): `sv_client_predict 0` is what `cl_predict` reads
    /// (`cdll_bounded_cvars.cpp:36-48`), and `CheckInitPredictable` returns at `!cl_predict->GetInt()` (`c_baseentity.cpp:2678`).
    /// The recorder's own launcher then draws the schedule's delay, 360, as anyone else's does.
    /// </summary>
    [Test]
    public void PropsAt_TheRecordersWeaponWithPredictionForcedOff_TakesTheSchedulesDelay()
    {
        ServerConVars off = new();
        off.Apply(new SetConVarMessage([new KeyValuePair<string, string>("sv_client_predict", "0")]));
        DemoTimeline timeline = DemoTimeline.ForTracks([Launcher(owner: 1)], recorder: 1, conVarChanges: [(0, off)]);
        List<SceneProp> props = [];

        timeline.PropsAt(60, props);

        props.ShouldHaveSingleItem().Pose.X.ShouldBe(360f, 0.01f);
    }

    /// <summary>The control for <see cref="PredictedEntityInterimTests"/>: a player nobody records takes the schedule, 360.</summary>
    [Test]
    public void PlayersAt_APlayerWhoIsNotTheRecorder_TakesTheSchedulesDelay()
    {
        ScenePlayer drawn = PredictedEntityInterimTests.SoldierAt60(recorder: null);

        drawn.X.ShouldBe(360f, 0.01f);
    }

    /// <summary>The control: with the delay constant the same crate is drawn at its target, 50 − 8 = 42, so 420.</summary>
    [Test]
    public void At_AMovingEntityAtAConstantDelay_IsDrawnAtItsTarget()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl")
        {
            Interpolation = InterpolationSchedule.Constant(Interval),
        };

        for (int tick = 0; tick <= 99; tick += 3)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });
        }

        crate.At(50)!.Value.X.ShouldBe(420f, 0.01f);
    }
}
