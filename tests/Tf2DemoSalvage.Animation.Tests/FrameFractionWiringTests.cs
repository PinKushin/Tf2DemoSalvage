using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The inter-frame fraction reaches the thing that samples bones.
/// </summary>
/// <remarks>
/// **The arithmetic and the wiring are separate defects and only one of them is loud** (B279).
/// `StudioSequences.FrameAt` can compute the engine's `s` perfectly and it changes nothing on
/// screen unless it arrives at the animation sampler — which is the shape of B268, B269 and B275
/// this session, three mechanisms whose hard half worked and whose call was missing.
///
/// So this asserts on what the sampler was HANDED. `CalcPoseSingle` samples every bone as
/// `CalcBoneQuaternion( iFrame, s, … )` (`bone_setup.cpp:915`), so `s` arriving is the whole
/// difference between an animation that steps through authored frames and one that moves.
/// </remarks>
public sealed class FrameFractionWiringTests
{
    [Test]
    public void Build_WithAFrameFraction_HandsItToTheAnimationSampler()
    {
        float? given = null;

        SkeletonPose pose = new(
            Bones(),
            (_, _, fraction, _) =>
            {
                given = fraction;
                return [];
            })
        {
            Sequence = 2,
            Frame = 5,
            FrameFraction = 0.25f,
        };

        pose.Build(boneMask: ~0, currentTime: 0d, new BoneAccessor(1), new BoneBitList(1));

        given.ShouldBe(0.25f, "the sampler blends with this; without it the animation steps");
    }

    /// <remarks>
    /// **The control**: a pose that was never given a fraction must hand across zero, which is the
    /// behaviour every caller had before this existed — a single-frame pose holder, and the last
    /// frame of a one-shot, both legitimately have nothing to blend toward.
    /// </remarks>
    [Test]
    public void Build_WithNoFrameFraction_HandsAcrossZero()
    {
        float? given = null;

        SkeletonPose pose = new(
            Bones(),
            (_, _, fraction, _) =>
            {
                given = fraction;
                return [];
            })
        {
            Sequence = 2,
            Frame = 5,
        };

        pose.Build(boneMask: ~0, currentTime: 0d, new BoneAccessor(1), new BoneBitList(1));

        given.ShouldBe(0f);
    }

    /// <summary>One root bone, which is the least a pose can be built over.</summary>
    private static IReadOnlyList<StudioBone> Bones() =>
        [
            new StudioBone(
                Name: "root",
                Parent: -1,
                Position: (0f, 0f, 0f),
                Rotation: (0f, 0f, 0f, 1f),
                PoseToBone: new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f },
                Flags: ~0),
        ];
}
