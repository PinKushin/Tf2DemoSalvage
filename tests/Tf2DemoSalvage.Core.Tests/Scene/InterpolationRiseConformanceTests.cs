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
    private static InterpolationSchedule RiseAtFifty()
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

        timeline.FrameStart(49);
        timeline.PropsAt(50, props);
        float atTheRise = props.ShouldHaveSingleItem().Pose.X;

        timeline.FrameStart(50);
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

        timeline.FrameStart(50);
        timeline.PropsAt(50, props);

        props.ShouldHaveSingleItem().Pose.X.ShouldBe(300f, 0.01f);
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
