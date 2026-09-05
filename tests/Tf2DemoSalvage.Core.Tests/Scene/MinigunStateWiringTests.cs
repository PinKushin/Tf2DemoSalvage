using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The minigun's wind-up state survives the hop from the wire to a drawn pose (B347).
/// </summary>
/// <remarks>
/// **The hop this suite exists for is the one the arithmetic tests cannot see.**
/// `MinigunBarrelConformanceTests` proves the spin is computed the way `UpdateBarrelMovement` does;
/// it says nothing about whether a demo's `m_iWeaponState` ever reaches the code that calls it. That
/// gap has shipped three no-ops in this repository already, and B346 hit it one field earlier — a
/// value carried on the prop track alone reached zero players.
///
/// **Synthetic rather than corpus, and the corpus is the reason** (D38). The demo checked for B347
/// carries 462 sends of `DT_WeaponMinigun.m_iWeaponState` — all four states — on entity 229, and
/// never DRAWS that entity: every minigun-model prop in it is a `CTFDroppedWeapon` lying on the
/// ground, which has no weapon state at all. A corpus assertion there would measure the demo's
/// roster rather than this project's wiring, and would have been written as a passing test of
/// nothing.
/// </remarks>
public sealed class MinigunStateWiringTests
{
    /// <summary>Entity slot the prop occupies.</summary>
    private const int Prop = 9;

    /// <remarks>
    /// **This is the REBUILD path, and naming it took a sabotage to get right.** `At` subtracts
    /// `InterpolationDelayTicks` (8) and then refuses any keyframe that has not arrived —
    /// `if (arrivedAt > tick) return from;` (`ScenePropTrack.cs:1492`). Asking at 660 makes
    /// `arrivedAt(660) > 660` false, so execution falls through into the field-by-field rebuild;
    /// that is where a discrete value gets dropped, and `HeadScale`, `TorsoScale` and `HandScale`
    /// all shipped lost through exactly it (B312).
    ///
    /// **The first version of this file had the two tests' claims the wrong way round**, asserting
    /// that a tick BETWEEN the keyframes exercised the rebuild. It does not, and only sabotaging
    /// the rebuild's assignment showed which test actually reddened.
    ///
    /// **`AC_STATE_SPINNING`, not idle.** Zero is a legal state and also the default of an `int?`
    /// that was never assigned, so a fixture using it could not tell "carried" from "dropped".
    /// </remarks>
    [Test]
    public void PropsAt_AtTheLaterKeyframe_CarriesTheStateThroughTheRebuild()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            (Tick: 0, Sequence: 0, Parity: 0),
            (Tick: 660, Sequence: 0, Parity: 0)));

        Pose(timeline, at: 660).MinigunState.ShouldBe(
            3,
            "the fixture sends AC_STATE_SPINNING, and the pose is what the renderer reads");
    }

    /// <remarks>
    /// **The OTHER path, and it is a different mechanism rather than a second sample of the same
    /// one.** A tick early enough that the later keyframe has not arrived returns that keyframe's
    /// pose object untouched — no rebuild happens at all, because a client at tick 330 cannot be
    /// pulled toward an update stated at 660. Both paths reach the renderer, so both need an
    /// assertion; a suite covering only the rebuild would miss a field dropped from the raw pose,
    /// and one covering only this would miss the rebuild.
    /// </remarks>
    [Test]
    public void PropsAt_BeforeTheLaterKeyframeArrives_CarriesTheStateFromTheEarlierPose()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            (Tick: 0, Sequence: 0, Parity: 0),
            (Tick: 660, Sequence: 1, Parity: 1)));

        Pose(timeline, at: 330).MinigunState.ShouldBe(
            3,
            "the earlier keyframe is what a client would be holding, and it says spinning");
    }

    /// <summary>The prop's pose at a tick, read the way the renderer reads it.</summary>
    private static ScenePose Pose(DemoTimeline timeline, double at)
    {
        System.Collections.Generic.List<SceneProp> drawn = [];
        timeline.PropsAt(at, drawn);

        foreach (SceneProp prop in drawn)
        {
            if (prop.EntityIndex == Prop)
            {
                return prop.Pose;
            }
        }

        throw new InvalidOperationException($"the fixture drew no prop {Prop} at {at}");
    }
}
