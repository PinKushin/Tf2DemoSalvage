using System.Linq;

using Tf2DemoSalvage.Core.Scene;

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
