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

        SkeletonPose pose = new(Bones(), (_, _, _) => [new StudioBonePose(0, (10f, 0f, 0f), (0f, 0f, 0f, 1f))])
        {
            Sequence = 4,
            SequenceBlend = sequence => new PoseLayer(sequence, 0f,1f, [weight]),
        };

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(1));

        into.BoneForWrite(0)[3].ShouldBe(expected, Tolerance);
    }

    /// <remarks>
    /// **`s2` is not clamped** (`bone_setup.cpp:1418`, against the clamp of `s` alone at `:1386-1389`): a bone weighed two
    /// extrapolates, `3 × (1 − 2) + 10 × 2 = 17`, where a clamp gives the sample, 10.
    /// </remarks>
    [Test]
    public void Build_AMainBoneWeighedTwo_Extrapolates()
    {
        BoneAccessor into = new(1);

        SkeletonPose pose = new(Bones(), (_, _, _) => [new StudioBonePose(0, (10f, 0f, 0f), (0f, 0f, 0f, 1f))])
        {
            Sequence = 4,
            SequenceBlend = sequence => new PoseLayer(sequence, 0f,1f, [2f]),
        };

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(1));

        into.BoneForWrite(0)[3].ShouldBe(17f, Tolerance);
    }

    /// <remarks>
    /// **The rotation is slerped from the bind too**: bind identity, sample a quarter turn about Z, weight one half — an
    /// eighth of a turn, so the matrix's first entry is cos 45°. A copy gives cos 90° = 0.
    /// </remarks>
    [Test]
    public void Build_AMainBoneWeighedAHalf_SlerpsItsRotationFromTheBind()
    {
        BoneAccessor into = new(1);
        float half = MathF.Sqrt(0.5f);

        SkeletonPose pose = new(
            [new StudioBone("root", -1, (0f, 0f, 0f), (0f, 0f, 0f, 1f), default, Flags: ~0)],
            (_, _, _) => [new StudioBonePose(0, (0f, 0f, 0f), (0f, 0f, half, half))])
        {
            Sequence = 4,
            SequenceBlend = sequence => new PoseLayer(sequence, 0f,1f, [0.5f]),
        };

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(1));

        into.BoneForWrite(0)[0].ShouldBe(half, Tolerance);
    }

    /// <remarks>
    /// **A main local layer composes into the sequence's own pose, which the main blend then weighs**
    /// (`bone_setup.cpp:2436-2440`). The main sequence weighs the bone zero and its local layer would move it to 50: the
    /// bone keeps the bind, 3. Composed onto the accumulator after the blend, as it was, it reaches 50.
    /// </remarks>
    [Test]
    public void Build_AMainLocalLayerOnABoneTheMainWeighsZero_KeepsTheBind()
    {
        BoneAccessor into = new(1);

        SkeletonPose pose = new(
            Bones(),
            (sequence, _, _) => [new StudioBonePose(0, (sequence == 4 ? 10f : 50f, 0f, 0f), (0f, 0f, 0f, 1f))])
        {
            Sequence = 4,
            SequenceBlend = sequence => sequence == 4 ? new PoseLayer(sequence, 0f,1f, [0f]) : null,
            Layers = [new PoseLayer(7, 0f,1f, [1f])],
            BaseLocal = 1,
            BaseNested = 1,
        };

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(1));

        into.BoneForWrite(0)[3].ShouldBe(3f, Tolerance);
    }

    /// <remarks>The control: with the main weighing the bone one, the local layer over it does reach 50.</remarks>
    [Test]
    public void Build_AMainLocalLayerOnABoneTheMainWeighsOne_ReachesTheLayer()
    {
        BoneAccessor into = new(1);

        SkeletonPose pose = new(
            Bones(),
            (sequence, _, _) => [new StudioBonePose(0, (sequence == 4 ? 10f : 50f, 0f, 0f), (0f, 0f, 0f, 1f))])
        {
            Sequence = 4,
            SequenceBlend = sequence => sequence == 4 ? new PoseLayer(sequence, 0f,1f, [1f], Delta: false) with { Weight = 1f } : null,
            Layers = [new PoseLayer(7, 0f,1f, [1f])],
            BaseLocal = 1,
            BaseNested = 1,
        };

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(1));

        into.BoneForWrite(0)[3].ShouldBe(50f, Tolerance);
    }

    /// <remarks>A delta main sequence ADDS onto the bind: the bind at 3 plus a sample of 10 is 13, where a copy is 10.</remarks>
    [Test]
    public void Build_ADeltaMainSequence_AddsOntoTheBind()
    {
        BoneAccessor into = new(1);

        SkeletonPose pose = new(Bones(), (_, _, _) => [new StudioBonePose(0, (10f, 0f, 0f), (0f, 0f, 0f, 1f))])
        {
            Sequence = 4,
            SequenceBlend = sequence => new PoseLayer(sequence, 0f,1f, [1f], Delta: true),
        };

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(1));

        into.BoneForWrite(0)[3].ShouldBe(13f, Tolerance);
    }

    /// <remarks>The control: with no blend for the sequence the sample is copied, as before.</remarks>
    [Test]
    public void Build_APlainMainSequence_TakesTheSample()
    {
        BoneAccessor into = new(1);

        SkeletonPose pose = new(Bones(), (_, _, _) => [new StudioBonePose(0, (10f, 0f, 0f), (0f, 0f, 0f, 1f))])
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
