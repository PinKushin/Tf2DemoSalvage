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
    /// **Origin and angles are separate variables** (B860 review 2, Fable): `NoteChanged` re-arms a variable only where its
    /// value changed (`interpolatedvar.h:633-639`, `c_baseentity.cpp:2828-2829`). The crate slides to 400 by 40 and its
    /// origin settles on the frame at 48 (aiming at 40, past every entry); a latch at 49 turns it without moving it. The
    /// origin was never re-armed, so after the rise it holds 400. Taking origin and angles as one variable re-armed both,
    /// and at 61 floored X at the oldest entry the frame at 50 kept (cut at 42 − 3.33: 38, 37, 36), 360.
    /// </summary>
    [Test]
    public void At_AnOriginSettledBeforeTheRiseAndALatchThatOnlyTurned_HoldsWhereItRested()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        for (int tick = 0; tick <= 40; tick++)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });
        }

        crate.Add(49, new ScenePose { X = 400f, Yaw = 90f });

        crate.At(61)!.Value.X.ShouldBe(400f);
    }

    /// <summary>
    /// **Pose parameters are their own variable too** (B860 review 2, Fable): stamped with the cycle, but settled by their
    /// own values. The cycle never moves: it settles on the frame at 28, aiming at its only entry, 20, and no later latch
    /// changes it, so it holds there. The pose parameter goes 0 at 20, then 0.5, 0.6, 0.7 at 36 to 38 and 1 at 44: re-armed
    /// and still blending at the rise, so at 60 it is floored at the oldest entry the frame at 50 kept (cut at 42 − 3.33:
    /// 38 and the two before it, 36), 0.5. Taking the cycle's held target drew it at 20, 0.
    /// </summary>
    [Test]
    public void At_APoseParameterStillBlendingWhenTheCycleSettled_IsFlooredOnItsOwn()
    {
        ScenePropTrack sentry = new(entityIndex: 1, "models/buildables/sentry1.mdl") { Interpolation = RiseAtFifty() };

        sentry.Add(20, new ScenePose { PoseParameters = [0f] });
        sentry.Add(36, new ScenePose { PoseParameters = [0.5f] });
        sentry.Add(37, new ScenePose { PoseParameters = [0.6f] });
        sentry.Add(38, new ScenePose { PoseParameters = [0.7f] });
        sentry.Add(44, new ScenePose { PoseParameters = [1f] });

        sentry.At(60)!.Value.PoseParameters[0].ShouldBe(0.5f);
    }

    /// <summary>
    /// **A held variable does not bring back the first pose's state** (B860 review 3, Fable): an entity that never moved
    /// holds every variable at its first entry after the rise, but its skin is a plain networked member, set on receipt.
    /// Born at 20, skin 1 from 40: at 60 it draws skin 1.
    /// </summary>
    [Test]
    public void At_AnEntityHeldAtItsFirstEntryAfterTheRise_KeepsItsLatestState()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        crate.Add(20, new ScenePose());
        crate.Add(40, new ScenePose { Skin = 1 });

        crate.At(60)!.Value.Skin.ShouldBe(1);
    }

    /// <summary>
    /// **A held variable keeps the value of the entry that last changed it** (B860 review 4, Fable): its settle frame drew
    /// that entry, and `Interp_Interpolate` skips it after (`c_baseentity.cpp:879-880`). X steps 0 to 200 by 20, then an
    /// update that arrives at 44 stamped 30 moves it to 300 and settles it at once (the frame at 44 aims at 36, past every
    /// entry); a latch at 49 only turns it. At 61 X holds 300 exactly. Sampled at the settle frame's target, 36, against
    /// today's history it fell between the entry at 30 and the turn's at 49, on a spline through 200: not 300.
    /// </summary>
    [Test]
    public void At_AHeldVariableAfterTheRise_KeepsTheEntryThatLastChangedIt()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        for (int tick = 0; tick <= 20; tick++)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });
        }

        crate.Add(44, new ScenePose { X = 300f }, appliedAt: 30);
        crate.Add(49, new ScenePose { X = 300f, Yaw = 90f });

        crate.At(61)!.Value.X.ShouldBe(300f);
    }

    /// <summary>
    /// **A rise does not send a drawn entity back to its birth pose** (B860 review 5, Fable): born at 40, skin 1 from 45.
    /// Frames to 50 drew it from its history (the frame at 50 aimed at 42); the rise sends the target back to 26, before
    /// birth, but the skin is a plain networked member, live from the latest update: 1, not the birth pose's 0.
    /// </summary>
    [Test]
    public void At_AYoungEntitySentBeforeItsBirthByTheRise_KeepsItsLatestState()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        crate.Add(40, new ScenePose());
        crate.Add(45, new ScenePose { Skin = 1 });

        crate.At(60)!.Value.Skin.ShouldBe(1);
    }

    /// <summary>
    /// **A held variable whose changing entry was flushed holds the value it changed to** (B860 review 7, Fable): X slides
    /// to 300 by 30, moves to 400 stamped 40 (arriving 41), and a same-valued latch stamped 39 (arriving 42) flushes it
    /// without re-arming; a turn at 49. The origin settled on the frame at 47 and is held: 400 at 51, where sampling at the
    /// flushed entry's changetime fell between 39 and 49 on a spline through 300.
    /// </summary>
    [Test]
    public void At_AHeldVariableWhoseChangingEntryWasFlushed_HoldsItsValue()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        for (int tick = 0; tick <= 30; tick++)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });
        }

        crate.Add(41, new ScenePose { X = 400f }, appliedAt: 40);
        crate.Add(42, new ScenePose { X = 400f }, appliedAt: 39);
        crate.Add(49, new ScenePose { X = 400f, Yaw = 90f });

        crate.At(51)!.Value.X.ShouldBe(400f);
    }

    /// <summary>
    /// **A pair starting on the pruned floor runs linear** (B860 review 7, Fable): the frame at 50 kept 36, 33 and 30 and
    /// dropped everything older, so between 30 and 33 there is no third sample (`interpolatedvar.h:782-795`, `:851`). X is
    /// 250 at 27, 300 at 30, 310 at 33, then 10 a tick. The frame at 65 aims at 31: one third of the way from 300 to 310,
    /// 303.33 — where a spline through 27's 250 overshoots.
    /// </summary>
    [Test]
    public void At_APairStartingOnThePrunedFloor_RunsLinear()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        for (int tick = 0; tick <= 27; tick += 3)
        {
            crate.Add(tick, new ScenePose { X = tick * 250f / 27f });
        }

        crate.Add(30, new ScenePose { X = 300f });

        for (int tick = 33; tick <= 99; tick += 3)
        {
            crate.Add(tick, new ScenePose { X = 310f + ((tick - 33) * 10f) });
        }

        crate.At(65)!.Value.X.ShouldBe(303.333f, 0.01f);
    }

    /// <summary>
    /// **A young entity whose delay rose is floored above its birth** (B860 review 4, Fable): born at 30, an update a tick.
    /// The frame at 50 aimed at 42 and kept 38, 37 and 36; the frame at 51 aims at 17, older than all of them, and answers
    /// the oldest, 36: 360. Gated on the delayed moment alone, it drew the first pose, 300.
    /// </summary>
    [Test]
    public void At_AYoungEntityAfterTheDelayRises_IsFlooredAboveItsBirth()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        for (int tick = 30; tick <= 99; tick++)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });
        }

        crate.At(51)!.Value.X.ShouldBe(360f);
    }

    /// <summary>
    /// **A moving entity cannot reach history the old delay pruned.** The crate slides on, an update every three ticks
    /// at ten units a tick. Frames a tick apart: the frame at 50 read the rise but still ran under the old delay, aiming at
    /// 42 and pruning before 42 − 3.33 = 38.67 (0.05 s at 0.015 s a tick): the first entry older than that is 36, kept with
    /// the two before it, 33 and 30. At 51 the new delay aims at 17; the oldest entry the engine still holds is 30, so it
    /// draws that entry, 300, not 170. (At 51 the stateless sampler and the frame model agree — B860 review 7, Sonnet.)
    /// </summary>
    [Test]
    public void At_AMovingEntityAfterTheDelayRises_ReachesNoFurtherBackThanTheOldDelayKept()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        for (int tick = 0; tick <= 99; tick += 3)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });
        }

        crate.At(51)!.Value.X.ShouldBe(300f);
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
    /// <remarks>
    /// A rewind or a scrub is a jump too, read the same way (B860 review 2, Sonnet): from negative infinity the frame at
    /// 180 still starts at 80, 1460. A jump landing inside the first hundred packets started having read the signon and
    /// the first packet at most (*interpolated*: where the skip's first batch ends is frame timing), so the frame at 60,
    /// whose first packet is at 0, runs under the first delay: 60 − 8 = 52, 520.
    /// </remarks>
    [TestCase(10d, 180, 1460f)]
    [TestCase(double.NegativeInfinity, 180, 1460f)]
    [TestCase(double.NegativeInfinity, 60, 520f)]
    public void PropsAt_AFrameAfterAJump_KnowsWhatTheLastSkipBatchRead(double lastRead, int frameTick, float expected)
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

        timeline.FrameStart(lastRead, frameTick);
        timeline.PropsAt(frameTick, props);

        props.ShouldHaveSingleItem().Pose.X.ShouldBe(expected, 0.01f);
    }

    /// <summary>
    /// **A jump never forgets the signon** (B860 review 3, Sonnet): the settings the recording states at its first packet
    /// are read when playback starts or restarts, before any skip counts a call. Packets from 50, where the delay already
    /// rose to 34; a jump to 100, fifty packets in, aims at 100 − 34 = 66, 660 — not at 100 − 8 = 92 under defaults, 920.
    /// </summary>
    [Test]
    public void PropsAt_AJumpInsideTheFirstHundredPackets_KnowsWhatTheFirstPacketStated()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };
        List<TimelineFrame> frames = [];

        for (int tick = 0; tick <= 249; tick++)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });

            if (tick >= 50)
            {
                frames.Add(new TimelineFrame(tick, []));
            }
        }

        DemoTimeline timeline = DemoTimeline.ForTracks([crate], frames);
        List<SceneProp> props = [];

        timeline.FrameStart(double.NegativeInfinity, 100);
        timeline.PropsAt(100, props);

        props.ShouldHaveSingleItem().Pose.X.ShouldBe(660f, 0.01f);
    }

    /// <summary>
    /// **A jump on a timeline with no packets reads its own tick** (B860 review 4, Sonnet; untested until review 5, Fable):
    /// there is nothing to place a skip batch by, so the frame at 60 runs under the delay of 60 like any reader that names
    /// no frame: the moving crate floored at 36, 360 — not under the first step, 52, 520.
    /// </summary>
    [Test]
    public void PropsAt_AJumpOnATimelineWithNoPackets_ReadsItsOwnTick()
    {
        ScenePropTrack crate = new(entityIndex: 1, "models/props_farm/wooden_barrel.mdl") { Interpolation = RiseAtFifty() };

        for (int tick = 0; tick <= 99; tick++)
        {
            crate.Add(tick, new ScenePose { X = tick * 10f });
        }

        DemoTimeline timeline = DemoTimeline.ForTracks([crate]);
        List<SceneProp> props = [];

        timeline.FrameStart(double.NegativeInfinity, 60);
        timeline.PropsAt(60, props);

        props.ShouldHaveSingleItem().Pose.X.ShouldBe(360f, 0.01f);
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
