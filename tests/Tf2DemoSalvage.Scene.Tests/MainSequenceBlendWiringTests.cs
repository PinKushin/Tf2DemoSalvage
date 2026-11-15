namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Which main sequences the skeleton composes rather than copies, and that every skeleton is asked (B740).</summary>
/// <remarks>
/// <c>SlerpBones</c> at weight 1 is a copy only for a non-delta sequence weighing every bone one
/// (<c>bone_setup.cpp:1405-1500</c>). A zero on a bone the sequence's group lacks is a copy too here, because the sample
/// already seeds that bone with the base's bind (B621).
/// </remarks>
public sealed class MainSequenceBlendWiringTests
{
    [TestCase(new[] { 1f, 1f }, false, false, true, TestName = "IsCopy_EveryBoneWeighedOne_IsACopy")]
    [TestCase(new[] { 1f, 0f }, false, false, false, TestName = "IsCopy_AMappedBoneWeighedZero_IsNotACopy")]
    [TestCase(new[] { 1f, 0f }, true, false, true, TestName = "IsCopy_AnUnmappedBoneWeighedZero_IsACopy")]
    [TestCase(new[] { 1f, 0.5f }, true, false, false, TestName = "IsCopy_APartialWeight_IsNotACopy")]
    [TestCase(new[] { 1f, 1f }, false, true, false, TestName = "IsCopy_ADelta_IsNotACopy")]
    public void IsCopy_AWeightList_IsACopyOnlyWhenSlerpBonesWouldCopy(float[] weights, bool secondUnmapped, bool delta, bool copy) =>
        PropModels.SkinnedModel.IsCopy(weights, secondUnmapped ? [0, -1] : [0, 1], delta).ShouldBe(copy);

    /// <remarks>The model's own sequence has no bone map, so every bone is its own and a zero is never a copy.</remarks>
    [TestCase(new[] { 1f, 0f }, false, TestName = "IsCopy_AZeroOnTheModelsOwnSequence_IsNotACopy")]
    [TestCase(new[] { 1f, 2f }, false, TestName = "IsCopy_AWeightAboveOne_IsNotACopy")]
    [TestCase(new[] { 1f, 1f }, true, TestName = "IsCopy_AllOnesOnTheModelsOwnSequence_IsACopy")]
    public void IsCopy_WithNoBoneMap_TreatsEveryBoneAsMapped(float[] weights, bool copy) =>
        PropModels.SkinnedModel.IsCopy(weights, null, delta: false).ShouldBe(copy);

    /// <remarks>The wiring: a skeleton the model makes asks the model, and a plain sequence answers a copy.</remarks>
    [Test]
    public void NewSkeleton_ForAModel_AsksItForTheSequencesBlend()
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With("idle");

        Animation.Animating.SkeletonPose skeleton = model.NewSkeleton();

        skeleton.SequenceBlend.ShouldNotBeNull();
        skeleton.SequenceBlend!(0).ShouldBeNull("the control: a sequence with no weight list is a copy");
    }
}
