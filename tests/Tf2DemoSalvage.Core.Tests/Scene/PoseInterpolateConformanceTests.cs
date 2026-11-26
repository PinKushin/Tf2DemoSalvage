using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The pose parameters and angles interpolate as `CInterpolatedVarArrayBase::_Interpolate` does (B870 parts 1, 3, 4).
/// </summary>
/// <remarks>
/// **`_Interpolate`** (`interpolatedvar.h:1310-1341`): a pair that is ONE entry is copied — the `start == end` quick exit at
/// `:1315` — and a pair of two is lerped per component, `LoopingLerp` where the element loops, with no compare of the values
/// first. The fraction is the variable's own: `GetInterpolationInfo` computes `frac` from that variable's own bracketing
/// entries (`:842-847`).
/// </remarks>
public sealed class PoseInterpolateConformanceTests
{
    private static readonly int Delay = ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

    /// <summary>
    /// **Two equal entries are lerped, not skipped**: a looping parameter at 1.0 in both blends to
    /// `LoopingLerp( ½, 1, 1 )`, which is `s − (int)s` of 1.0, so 0 — and the other parameter holds 0.2.
    /// </summary>
    /// <remarks>
    /// Entries at 0, 1, 2 carry (0.2, 1.0), (0.2, 1.0), (0.8, 0.5), the second parameter looping; the target is 0.5. The
    /// viewer returned the arrival keyframe whenever the delayed pair agreed, which drew (0.8, 0.5) a delay early.
    /// </remarks>
    [Test]
    public void At_AnEqualPairWithALoopingParameterAtOne_LerpsItToZero()
    {
        ScenePropTrack track = Track([false, true], (0, [0.2f, 1.0f]), (1, [0.2f, 1.0f]), (2, [0.8f, 0.5f]));

        float[] drawn = [.. track.At(0.5d + Delay)!.Value.PoseParameters];

        drawn[0].ShouldBe(0.2f);
        drawn[1].ShouldBe(0f);
    }

    /// <summary>
    /// **One entry is copied**: the same looping parameter at 1.0, the target past the only entry, draws 1.0 — the quick
    /// exit runs before any lerp, and a lerp of 1.0 with itself would give 0.
    /// </summary>
    [Test]
    public void At_ALoopingParameterAtOneWithTheTargetPastItsOnlyEntry_DrawsOne()
    {
        ScenePropTrack track = Track([true], (0, [1.0f]));

        track.At(20d + Delay)!.Value.PoseParameters[0].ShouldBe(1f);
    }

    /// <summary>The control for the one above: two DISTINCT entries both at 1.0 are a pair, and a pair lerps to 0.</summary>
    [Test]
    public void At_ALoopingParameterAtOneInTwoEntries_DrawsZero()
    {
        ScenePropTrack track = Track([true], (0, [1.0f]), (10, [1.0f]));

        track.At(5d + Delay)!.Value.PoseParameters[0].ShouldBe(0f);
    }

    /// <summary>
    /// **One angle entry holding a NaN draws as itself**: the quick exit copies it, so the yaw stays 30 where a slerp of
    /// a NaN pitch would make every component NaN.
    /// </summary>
    [Test]
    public void At_OneAngleEntryWithANaNPitch_DrawsItsOwnYaw()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door.mdl");

        track.Add(0, new ScenePose { Pitch = float.NaN, Yaw = 30f });

        ScenePose drawn = track.At(20d + Delay)!.Value;

        drawn.Yaw.ShouldBe(30f);
        float.IsNaN(drawn.Pitch).ShouldBeTrue();
    }

    /// <summary>
    /// **The pose parameters take their OWN pair's fraction, not the cycle's.** A sequence restart at 100 resets the
    /// cycle's history and not theirs, so at a target of 101 the cycle pair (100, 104) gives 0.25 and the pose pair
    /// (96, 104) gives 0.625.
    /// </summary>
    [Test]
    public void At_ATargetAfterASequenceRestart_BlendsPoseParametersOnTheirOwnPair()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door.mdl");

        track.Add(96, new ScenePose { PoseParameters = [0f] }, appliedAt: 96, animationAppliedAt: 96);

        // The update carrying the restart says nothing new about the animation time, so it appends no animation entry.
        track.Add(100, new ScenePose { X = 1f, PoseParameters = [0f] }, appliedAt: 100, animationAppliedAt: 96);
        track.SequenceRestarted(100);

        track.Add(104, new ScenePose { X = 2f, PoseParameters = [1f] }, appliedAt: 104, animationAppliedAt: 104);

        track.At(101d + Delay)!.Value.PoseParameters[0].ShouldBe(0.625f, 1e-6f);
    }

    /// <summary>A track told its model at the first update, as the build does, then given the rest.</summary>
    private static ScenePropTrack Track(bool[] looping, params (int Tick, float[] Pose)[] updates)
    {
        ScenePropTrack track = new(entityIndex: 3, "models/buildables/sentry1.mdl");

        for (int index = 0; index < updates.Length; index++)
        {
            (int tick, float[] pose) = updates[index];

            track.Add(tick, new ScenePose { PoseParameters = pose });

            if (index == 0)
            {
                track.OnNewModel(looping, at: tick, staticPropModel: false);
            }
        }

        return track;
    }
}
