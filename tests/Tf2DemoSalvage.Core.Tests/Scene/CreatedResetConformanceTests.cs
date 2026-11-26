using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A created entity's interpolation histories are reset at the arrival (B871): `ResetLatched` on `DATA_UPDATE_CREATED`.
/// </summary>
/// <remarks>
/// **`C_BaseEntity::PostDataUpdate`** latches the update (`c_baseentity.cpp:2583-2591`), sets the model
/// (`ValidateModelIndex`, `:2609`), and then, for a created entity, calls `ResetLatched()` (`:2612-2617`), which is
/// `Interp_Reset`: `Reset()` on every interpolated variable (`:5866-5884`) — `ClearHistory()` and three entries stamped
/// `gpGlobals->curtime` with the values just unpacked (`interpolatedvar.h:740-751`). `C_BaseAnimating::OnDataChanged`
/// does it again for a created entity (`c_baseanimating.cpp:5004-5006`), the same values at the same time. So the latch's
/// own stamp — the simulation or animation time — is discarded: the first pair runs from the ARRIVAL.
/// </remarks>
public sealed class CreatedResetConformanceTests
{
    private static readonly int Delay = ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

    /// <summary>
    /// **The first pose-parameter pair starts at the arrival, not the animation time**: created at 10 with an animation
    /// time 3 behind, then 0.6 at 20 (animation time 17). At a target of 12 the pair is 10 → 17, so 2/7 of the way: 0.3143.
    /// Stamped with the animation time it would be 7 → 17, half way: 0.4.
    /// </summary>
    [Test]
    public void At_TheFirstPairOfACreatedEntity_StartsAtTheArrival()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticAnimatingProp.Demo(
            [string.Empty, "models/props_gameplay/door.mdl"],
            animationLag: 3,
            (10, 1, [0.2f], 0.2f, null),
            (20, 1, [0.6f], 0.6f, null)));

        ScenePose drawn = timeline.Props.Single(track => track.EntityIndex == SyntheticAnimatingProp.EntityIndex)
            .At(12d + Delay)!.Value;

        drawn.PoseParameters[0].ShouldBe(0.2f + (0.4f * 2f / 7f), 1e-5f);
        drawn.Cycle.ShouldBe(0.2f + (0.4f * 2f / 7f), 1e-5f, "the cycle's history is reset too");
    }

    /// <summary>
    /// **A track that begins after its entity was created is not reset** (B871 reviews 1-2): an entity created at 10 with no
    /// model gets its first track at 50, but the update at 50 is not `DATA_UPDATE_CREATED`, so its CYCLE only latches
    /// (animation time 47). With 1.0 at 60 (57), a target of 52 is half way from 47: 0.8 — a reset at 50 gives 2/7 of the way.
    /// The cycle, because setting the model at 50 runs `OnNewModel`, whose pose-parameter `SetMaxCount` the engine DOES reset
    /// on (`c_baseanimating.cpp:1124`); `m_iv_flCycle` it leaves alone.
    /// </summary>
    [Test]
    public void At_ATrackBegunAfterItsEntitysCreation_IsNotReset()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticAnimatingProp.Demo(
            [string.Empty, "models/props_gameplay/door.mdl"],
            animationLag: 3,
            (10, 0, [], 0.2f, null),
            (50, 1, [], 0.6f, null),
            (60, 1, [], 1.0f, null)));

        ScenePropTrack track = timeline.Props.Single(track => track.EntityIndex == SyntheticAnimatingProp.EntityIndex);

        track.FirstTick.ShouldBe(50, "the control: no track while the entity had no model, so its first update is a delta");
        track.At(52d + Delay)!.Value.Cycle.ShouldBe(0.8f, 1e-5f);
    }

    /// <summary>
    /// **An `Enter` that re-enters an entity is not a creation, even when it begins the track** (B871 review 3): created at
    /// 10 with no model (no track), out of the visible set at 30, back at 50 with the SAME serial and a model. The engine holds
    /// the dormant entity, so 50 latches (animation time 47) and does not reset: cycle 0.8 at a target of 52, where a reset at
    /// 50 gives 0.714.
    /// </summary>
    [Test]
    public void At_ATrackBegunByARentryOfTheSameSerial_IsNotReset()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticAnimatingProp.Demo(
            [string.Empty, "models/props_gameplay/door.mdl"],
            animationLag: 3,
            [EntityUpdateType.Enter, EntityUpdateType.Leave, EntityUpdateType.Enter, EntityUpdateType.Delta],
            (10, 0, [], 0.2f, null),
            (30, 0, [], null, null),
            (50, 1, [], 0.6f, null),
            (60, 1, [], 1.0f, null)));

        ScenePropTrack track = timeline.Props.Single(track => track.EntityIndex == SyntheticAnimatingProp.EntityIndex);

        track.FirstTick.ShouldBe(50, "the control: the track begins at the re-entry");
        track.At(52d + Delay)!.Value.Cycle.ShouldBe(0.8f, 1e-5f);
    }

    /// <summary>
    /// **A full packet recreates the entity, so it resets even with the same serial** (B871 review 3): the engine destroys
    /// every client entity on a non-delta update and recreates them (`iclientnetworkable.h:99-101`). Created at 10, restated
    /// at 50 in a full packet (animation time 47), 1.0 at 60 (57): a target of 52 is 2/7 of the way from 50, 0.714. The
    /// control is the same frames with 50 in a delta packet, which only latches: it must draw what a track given the same
    /// three latches (7, 47, 57) and no reset draws — the hermite through all three, whatever its value.
    /// </summary>
    [Test]
    public void At_ASameSerialEnterInAFullPacket_StartsAtTheArrival()
    {
        float Drawn(bool full) => DemoTimeline.Build(SyntheticAnimatingProp.Demo(
                [string.Empty, "models/props_gameplay/door.mdl"],
                animationLag: 3,
                [EntityUpdateType.Enter, EntityUpdateType.Enter, EntityUpdateType.Delta],
                full ? new HashSet<int> { 1 } : null,
                (10, 1, [], 0.2f, null),
                (50, 1, [], 0.6f, null),
                (60, 1, [], 1.0f, null)))
            .Props.Single(track => track.EntityIndex == SyntheticAnimatingProp.EntityIndex)
            .At(52d + Delay)!.Value.Cycle;

        ScenePropTrack unreset = new(SyntheticAnimatingProp.EntityIndex, "models/props_gameplay/door.mdl");
        unreset.Add(10, new ScenePose { Cycle = 0.2f }, appliedAt: 10, animationAppliedAt: 7);
        unreset.ResetLatched(10);
        unreset.Add(50, new ScenePose { Cycle = 0.6f }, appliedAt: 50, animationAppliedAt: 47);
        unreset.Add(60, new ScenePose { Cycle = 1.0f }, appliedAt: 60, animationAppliedAt: 57);
        float latched = unreset.At(52d + Delay)!.Value.Cycle;

        Math.Abs(latched - (0.6f + (0.4f * 2f / 7f))).ShouldBeGreaterThan(1e-2f, "the instrument: a latch must not draw what a reset draws");
        Drawn(full: false).ShouldBe(latched, 1e-5f, "the control: a delta packet's re-entry only latches");
        Drawn(full: true).ShouldBe(0.6f + (0.4f * 2f / 7f), 1e-5f);
    }

    /// <summary>
    /// **The simulation history too**: X 100 arriving at 10 with a simulation time of 7, then 200 at 20 (simulation time
    /// 17). At a target of 12: 100 + 100 × 2/7 = 128.57, where a pair from 7 gives 150.
    /// </summary>
    [Test]
    public void ResetLatched_ACreatedEntitysSimulationHistory_StartsAtTheArrival()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door.mdl");

        track.Add(10, new ScenePose { X = 100f }, appliedAt: 7, animationAppliedAt: 7);
        track.ResetLatched(10);
        track.Add(20, new ScenePose { X = 200f }, appliedAt: 17, animationAppliedAt: 17);

        track.At(12d + Delay)!.Value.X.ShouldBe(100f + (100f * 2f / 7f), 1e-3f);
    }
}
