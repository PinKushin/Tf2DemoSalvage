using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The cycle reaches the thing that samples bones.
/// </summary>
/// <remarks>
/// **The arithmetic and the wiring are separate defects and only one of them is loud** (B279). The sampler can turn a
/// cycle into the engine's frame and <c>s</c> perfectly and it changes nothing on screen unless the cycle arrives — the
/// shape of B268, B269 and B275, three mechanisms whose hard half worked and whose call was missing.
///
/// So this asserts on what the sampler was HANDED. **The cycle, not a frame** (B742): `CalcPoseSingle` hands the cycle to
/// each blend-grid corner's `CalcAnimation` (`bone_setup.cpp:1993-2010`), which finds its own frame from its own count
/// (`:913-917`).
/// </remarks>
public sealed class CycleWiringTests
{
    [Test]
    public void Build_WithACycle_HandsItToTheAnimationSampler()
    {
        float? given = null;

        SkeletonPose pose = new(
            Bones(),
            (_, cycle, _) =>
            {
                given = cycle;
                return [];
            })
        {
            Sequence = 2,
            Cycle = 0.25f,
        };

        pose.Build(boneMask: ~0, currentTime: 0d, new BoneAccessor(1), new BoneBitList(1));

        given.ShouldBe(0.25f, "the sampler finds each animation's frame from this");
    }

    /// <remarks>**The control**: a pose never given a cycle hands across zero, the sequence's start.</remarks>
    [Test]
    public void Build_WithNoCycle_HandsAcrossZero()
    {
        float? given = null;

        SkeletonPose pose = new(
            Bones(),
            (_, cycle, _) =>
            {
                given = cycle;
                return [];
            })
        {
            Sequence = 2,
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
