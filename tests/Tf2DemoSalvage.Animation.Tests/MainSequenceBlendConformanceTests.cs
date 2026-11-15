using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>The main sequence is composed onto the bind pose through <c>SlerpBones</c>, as a layer is (B740).</summary>
/// <remarks>
/// **Valve:** <c>StandardBlendingRules</c> seeds <c>InitPose</c>'s bind and runs <c>AccumulatePose</c> on the main
/// sequence at weight 1, which ends in <c>SlerpBones</c>: per bone <c>s2 = 1 · seqdesc.weight(j)</c>, a bone at or below
/// zero is skipped and keeps the bind, any other is slerped toward the sample by <c>s2</c>; a <c>STUDIO_DELTA</c>
/// sequence is added onto the bind instead (<c>bone_setup.cpp:1405-1500</c>). Ours copied the main sample in whole.
/// 6,893 shipped non-delta sequences weigh some bone other than one (<c>sequence-weights</c>, 2026-11-15).
/// </remarks>
public sealed class MainSequenceBlendConformanceTests
{
    private const float Tolerance = 1e-4f;

    /// <remarks>
    /// The bind is at 3, the sample at 10. Weight 1 copies (10); weight 0 keeps the bind (3); a half lerps the position
    /// (6.5). A plain sequence — no blend given — is the copy, the control.
    /// </remarks>
    [TestCase(1f, 10f, TestName = "Build_AMainBoneWeighedOne_TakesTheSample")]
    [TestCase(0f, 3f, TestName = "Build_AMainBoneWeighedZero_KeepsTheBind")]
    [TestCase(0.5f, 6.5f, TestName = "Build_AMainBoneWeighedAHalf_BlendsFromTheBind")]
    public void Build_AMainSequenceWithBoneWeights_ComposesOntoTheBind(float weight, float expected)
    {
        BoneAccessor into = new(1);

        SkeletonPose pose = new(Bones(), (_, _, _, _) => [new StudioBonePose(0, (10f, 0f, 0f), (0f, 0f, 0f, 1f))])
        {
            Sequence = 4,
            SequenceBlend = sequence => new PoseLayer(sequence, 0, 0f, 1f, [weight]),
        };

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(1));

        into.BoneForWrite(0)[3].ShouldBe(expected, Tolerance);
    }

    /// <remarks>A delta main sequence ADDS onto the bind: the bind at 3 plus a sample of 10 is 13, where a copy is 10.</remarks>
    [Test]
    public void Build_ADeltaMainSequence_AddsOntoTheBind()
    {
        BoneAccessor into = new(1);

        SkeletonPose pose = new(Bones(), (_, _, _, _) => [new StudioBonePose(0, (10f, 0f, 0f), (0f, 0f, 0f, 1f))])
        {
            Sequence = 4,
            SequenceBlend = sequence => new PoseLayer(sequence, 0, 0f, 1f, [1f], Delta: true),
        };

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(1));

        into.BoneForWrite(0)[3].ShouldBe(13f, Tolerance);
    }

    /// <remarks>The control: with no blend for the sequence the sample is copied, as before.</remarks>
    [Test]
    public void Build_APlainMainSequence_TakesTheSample()
    {
        BoneAccessor into = new(1);

        SkeletonPose pose = new(Bones(), (_, _, _, _) => [new StudioBonePose(0, (10f, 0f, 0f), (0f, 0f, 0f, 1f))])
        {
            Sequence = 4,
            SequenceBlend = _ => null,
        };

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(1));

        into.BoneForWrite(0)[3].ShouldBe(10f, Tolerance);
    }

    private static IReadOnlyList<StudioBone> Bones() =>
        [new StudioBone("root", -1, (3f, 0f, 0f), (0f, 0f, 0f, 1f), default, Flags: ~0)];
}
